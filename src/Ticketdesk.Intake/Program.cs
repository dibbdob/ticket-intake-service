using System.Diagnostics;
using System.Text.Json.Serialization;
using Anthropic;
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

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", mode, model }));

app.MapPost("/tickets", async (NewTicket body, IExtractor extractor, TicketStore store, Telemetry telemetry, TimeProvider clock, CancellationToken ct) =>
{
    using var span = Telemetry.Source.StartActivity("extract ticket");
    span?.SetTag("extraction.model", model);
    var started = Stopwatch.GetTimestamp();

    ExtractionResult result;
    try
    {
        result = await extractor.Extract(body.Email, ct);
    }
    catch (ExtractionFailedException failure)
    {
        telemetry.Failed(failure, Stopwatch.GetElapsedTime(started).TotalSeconds);
        span?.SetStatus(ActivityStatusCode.Error, failure.Kind.ToString());
        throw;
    }

    var e = result.Extraction;
    var received = clock.GetUtcNow();
    var priority = Rules.PriorityOf(e);
    var ticket = store.Add(id => new StoredTicket(id, received, e, priority, Rules.DueAt(received, priority), e.NeedsHuman, result.AnsweredBy, result.Attempts));

    telemetry.Extracted(result, priority, Stopwatch.GetElapsedTime(started).TotalSeconds);
    span?.SetTag("ticket.id", ticket.Id);
    span?.SetTag("ticket.priority", priority);
    span?.SetTag("extraction.attempts", result.Attempts);
    span?.SetTag("extraction.answered_by", result.AnsweredBy);
    return Results.Created($"/tickets/{ticket.Id}", ticket);
});

app.MapGet("/tickets/{id}", (string id, TicketStore store) => store.Find(id) is { } t ? Results.Ok(t) : Results.NotFound());

app.Run();

public sealed record NewTicket([property: JsonPropertyName("email")] string Email);

public partial class Program;
