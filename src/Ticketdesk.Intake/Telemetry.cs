namespace Ticketdesk.Intake;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Ticketdesk.Extraction;
using Ticketdesk.Extraction.Evaluation;

/// <summary>
/// What the service tells an operator. Nothing here carries what a customer wrote: spans and metrics hold
/// the outcome, the model, the counts and the cost, never the email or the summary.
/// </summary>
public sealed class Telemetry : IDisposable
{
    public const string Name = "Ticketdesk.Intake";

    public static readonly ActivitySource Source = new(Name);

    private readonly Meter meter = new(Name);
    private readonly Counter<long> received;
    private readonly Counter<long> failures;
    private readonly Counter<long> tokens;
    private readonly Counter<double> cost;
    private readonly Histogram<double> duration;

    public Telemetry()
    {
        this.received = this.meter.CreateCounter<long>("ticketdesk.tickets.received", description: "Tickets stored, by priority and whether a person must see them.");
        this.failures = this.meter.CreateCounter<long>("ticketdesk.extraction.failures", description: "Emails that could not be extracted, by why.");
        this.tokens = this.meter.CreateCounter<long>("ticketdesk.extraction.tokens", description: "Tokens used, by model and kind (input, output, cache_read, cache_write).");
        this.cost = this.meter.CreateCounter<double>("ticketdesk.extraction.cost", unit: "USD", description: "Estimated cost at list prices, by model.");
        this.duration = this.meter.CreateHistogram<double>("ticketdesk.extraction.duration", unit: "s", description: "Time from email to extraction, retries included.");
    }

    public void Extracted(ExtractionResult result, string priority, double seconds)
    {
        this.received.Add(1, new("priority", priority), new("for_a_person", result.Extraction.NeedsHuman));
        this.duration.Record(seconds, new KeyValuePair<string, object?>("outcome", "extracted"));
        foreach (var spend in result.Spend)
        {
            this.Spent(spend);
        }
    }

    public void Failed(ExtractionFailedException failure, double seconds)
    {
        this.failures.Add(1, new KeyValuePair<string, object?>("kind", failure.Kind.ToString()));
        this.duration.Record(seconds, new KeyValuePair<string, object?>("outcome", "failed"));
        foreach (var spend in failure.Spend)
        {
            this.Spent(spend);
        }
    }

    public void Dispose() => this.meter.Dispose();

    private void Spent(ModelSpend spend)
    {
        var model = new KeyValuePair<string, object?>("model", spend.Model);
        this.tokens.Add(spend.Usage.Input, model, new("kind", "input"));
        this.tokens.Add(spend.Usage.Output, model, new("kind", "output"));
        this.tokens.Add(spend.Usage.CacheRead, model, new("kind", "cache_read"));
        this.tokens.Add(spend.Usage.CacheWrite, model, new("kind", "cache_write"));

        // The scripted gateway has no price, and a cost of zero for it is the truth.
        if (Prices.PerMillionTokens.TryGetValue(spend.Model, out var price))
        {
            this.cost.Add((double)price.Cost(spend.Usage), model);
        }
    }
}
