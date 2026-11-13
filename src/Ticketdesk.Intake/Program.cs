using System.Text.Json.Serialization;
using Anthropic;
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
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", mode, model }));

app.MapPost("/tickets", async (NewTicket body, IExtractor extractor, TicketStore store, TimeProvider clock, CancellationToken ct) =>
{
    var result = await extractor.Extract(body.Email, ct);
    var e = result.Extraction;
    var received = clock.GetUtcNow();
    var priority = Rules.PriorityOf(e);
    var ticket = store.Add(id => new StoredTicket(id, received, e, priority, Rules.DueAt(received, priority), e.NeedsHuman, result.AnsweredBy, result.Attempts));
    return Results.Created($"/tickets/{ticket.Id}", ticket);
});

app.MapGet("/tickets/{id}", (string id, TicketStore store) => store.Find(id) is { } t ? Results.Ok(t) : Results.NotFound());

app.Run();

public sealed record NewTicket([property: JsonPropertyName("email")] string Email);

public partial class Program;
