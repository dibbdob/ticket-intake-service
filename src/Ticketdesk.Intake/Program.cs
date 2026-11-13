using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Anthropic;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Ticketdesk.Extraction;
using Ticketdesk.Intake;

var builder = WebApplication.CreateBuilder(args);

// live: the Claude API (credentials from ANTHROPIC_API_KEY). scripted: no model, no key, no cost.
var mode = builder.Configuration["Extraction:Mode"] ?? "scripted";
var model = builder.Configuration["Extraction:Model"] ?? "claude-haiku-5-5";
var concurrency = builder.Configuration.GetValue("Extraction:Concurrency", 4);
builder.Services.AddSingleton<IModelGateway>(_ => mode switch
{
    "live" => new AnthropicGateway(new AnthropicClient()),
    "scripted" => new ScriptedGateway(),
    _ => throw new InvalidOperationException($"Extraction:Mode must be live or scripted, not '{mode}'."),
});
builder.Services.AddSingleton<IExtractor>(sp => new TicketExtractor(sp.GetRequiredService<IModelGateway>(), model));
builder.Services.AddSingleton<TicketStore>();
builder.Services.AddSingleton<Telemetry>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<Intake>(sp, model));
builder.Services.AddProblemDetails();

// At most `concurrency` extractions call the model at once, and a short queue behind them. Beyond that the
// caller is told to come back, rather than every request waiting on a rate-limited API.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;
    o.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "5";
        return ValueTask.CompletedTask;
    };
    o.AddConcurrencyLimiter("extraction", l =>
    {
        l.PermitLimit = concurrency;
        l.QueueLimit = concurrency * 4;
        l.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// Traces for every request and every outbound HTTP call (the Anthropic SDK's included), and the service's
// own metrics. Exported over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set; otherwise collected and dropped.
var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ticketdesk-intake"))
    .WithTracing(t => t.AddSource(Telemetry.Name).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(m => m.AddMeter(Telemetry.Name).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    otel.UseOtlpExporter();
}

var app = builder.Build();
app.UseRateLimiter();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", mode, model }));

app.MapPost("/tickets", async (NewTicket body, HttpRequest request, Intake intake, TicketStore store, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.Email))
    {
        return Results.Problem(statusCode: 400, title: "The email is empty.");
    }

    if (body.Email.Length > NewTicket.MaxLength)
    {
        return Results.Problem(statusCode: 413, title: $"The email is {body.Email.Length} characters; the limit is {NewTicket.MaxLength}.");
    }

    // A client that retries after a timeout sends the same key, and gets the ticket it already created, even
    // when the retry arrives while the first request is still being extracted.
    var key = request.Headers["Idempotency-Key"].ToString();
    StoredTicket ticket;
    if (key.Length > 0)
    {
        var keyed = await store.ForKey(key, body.Email, () => intake.Receive(body.Email, ct));
        if (keyed.Conflict)
        {
            return Results.Problem(statusCode: 422, title: "This Idempotency-Key was used for a different email.");
        }

        if (!keyed.Created)
        {
            return Results.Ok(keyed.Ticket);
        }

        ticket = keyed.Ticket!;
    }
    else
    {
        ticket = await intake.Receive(body.Email, ct);
    }

    // 201 when the ticket is extracted; 202 when it is kept for a person to read first.
    return ticket.Status == "extracted"
        ? Results.Created($"/tickets/{ticket.Id}", ticket)
        : Results.Accepted($"/tickets/{ticket.Id}", ticket);
}).RequireRateLimiting("extraction");

app.MapGet("/tickets/{id}", (string id, TicketStore store) => store.Find(id) is { } t ? Results.Ok(t) : Results.NotFound());

app.Run();

public sealed record NewTicket([property: JsonPropertyName("email")] string Email)
{
    public const int MaxLength = 20_000;
}

public partial class Program;
