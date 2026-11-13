namespace Ticketdesk.Intake.Tests;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;

/// <summary>Listens to the service's own meter and source while a test runs.</summary>
public sealed class Recorder : IDisposable
{
    private readonly MeterListener meters = new();
    private readonly ActivityListener spans;

    public Recorder()
    {
        this.meters.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == Telemetry.Name)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        this.meters.SetMeasurementEventCallback<long>((i, v, tags, _) => this.Measurements.Add((i.Name, v, Tags(tags))));
        this.meters.SetMeasurementEventCallback<double>((i, v, tags, _) => this.Measurements.Add((i.Name, v, Tags(tags))));
        this.meters.Start();

        this.spans = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Telemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => this.Spans.Add(a),
        };
        ActivitySource.AddActivityListener(this.spans);
    }

    public ConcurrentBag<(string Name, double Value, Dictionary<string, string?> Tags)> Measurements { get; } = [];

    public ConcurrentBag<Activity> Spans { get; } = [];

    public void Dispose()
    {
        this.meters.Dispose();
        this.spans.Dispose();
    }

    private static Dictionary<string, string?> Tags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var d = new Dictionary<string, string?>();
        foreach (var t in tags)
        {
            d[t.Key] = t.Value?.ToString();
        }

        return d;
    }
}

[NotInParallel]
public sealed class TelemetryTests
{
    private const string Email = "Every call to the API returns 503 since 09:10 and our checkout at secret-shop.example is offline. Enterprise.";

    [Test]
    public async Task A_stored_ticket_is_counted_by_priority_with_its_tokens_and_how_long_it_took()
    {
        using var recorder = new Recorder();
        await using var service = new Service();

        await service.CreateClient().PostAsJsonAsync("/tickets", new { email = Email });

        var received = recorder.Measurements.Single(m => m.Name == "ticketdesk.tickets.received");
        await Assert.That(received.Tags["priority"]).IsEqualTo("urgent");
        await Assert.That(recorder.Measurements.Any(m => m.Name == "ticketdesk.extraction.tokens" && m.Tags["kind"] == "output" && m.Value == 60)).IsTrue();
        await Assert.That(recorder.Measurements.Any(m => m.Name == "ticketdesk.extraction.duration" && m.Tags["outcome"] == "extracted")).IsTrue();
    }

    [Test]
    public async Task The_scripted_gateway_costs_nothing_rather_than_failing_to_be_priced()
    {
        using var recorder = new Recorder();
        await using var service = new Service();

        var response = await service.CreateClient().PostAsJsonAsync("/tickets", new { email = Email });

        await Assert.That((int)response.StatusCode).IsEqualTo(201);
        await Assert.That(recorder.Measurements.Any(m => m.Name == "ticketdesk.extraction.cost")).IsFalse();
    }

    [Test]
    public async Task Nothing_the_customer_wrote_reaches_a_span_or_a_metric()
    {
        using var recorder = new Recorder();
        await using var service = new Service();

        await service.CreateClient().PostAsJsonAsync("/tickets", new { email = Email });

        var span = recorder.Spans.Single(s => s.OperationName == "extract ticket");
        await Assert.That(span.GetTagItem("ticket.priority")).IsEqualTo("urgent");
        var everything = string.Join(" ", recorder.Spans.SelectMany(s => s.Tags.Select(t => t.Value)))
            + string.Join(" ", recorder.Measurements.SelectMany(m => m.Tags.Values));
        await Assert.That(everything).DoesNotContain("secret-shop").And.DoesNotContain("checkout");
    }
}
