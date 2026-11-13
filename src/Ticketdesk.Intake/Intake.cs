namespace Ticketdesk.Intake;

using System.Diagnostics;
using Anthropic.Exceptions;
using Ticketdesk.Extraction;

/// <summary>
/// Takes one email in and always stores it. Extraction is how the desk gets ahead of an email; it is not
/// a condition for keeping one. If the model refuses, cannot be reached, or produces something the rules
/// reject, the email is stored unextracted, at normal priority, for a person.
/// </summary>
public sealed class Intake(IExtractor extractor, TicketStore store, Telemetry telemetry, TimeProvider clock, ILogger<Intake> log, string model)
{
    public async Task<StoredTicket> Receive(string email, CancellationToken ct)
    {
        using var span = Telemetry.Source.StartActivity("extract ticket");
        span?.SetTag("extraction.model", model);
        var started = Stopwatch.GetTimestamp();
        var received = clock.GetUtcNow();

        StoredTicket ticket;
        try
        {
            var result = await extractor.Extract(email, ct);
            var e = result.Extraction;
            var priority = Rules.PriorityOf(e);
            ticket = store.Add(id => new StoredTicket(id, received, "extracted", e, null, priority, Rules.DueAt(received, priority), e.NeedsHuman, result.AnsweredBy, result.Attempts) { Email = email });
            telemetry.Extracted(result, priority, Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
        catch (ExtractionFailedException failure)
        {
            telemetry.Failed(failure, Stopwatch.GetElapsedTime(started).TotalSeconds);
            ticket = this.Unextracted(email, received, failure.Kind.ToString(), failure.Attempts);
        }
        catch (Exception error) when (error is AnthropicException or HttpRequestException or TimeoutException or OperationCanceledException)
        {
            // The model could not be reached, or the API refused the call (rate limit, overload, an expired
            // key). Retrying is the SDK's job and it has already done so; the email still has to be kept.
            // HttpClient reports its own timeout as a cancellation, so a cancelled call is kept the same way,
            // as is one whose caller went away: the email arrived, and a retry with its key will find it.
            telemetry.Unavailable(Stopwatch.GetElapsedTime(started).TotalSeconds);
            ticket = this.Unextracted(email, received, "ModelUnavailable", 0);
            log.LogWarning("Model unavailable for an email: {ErrorType}", error.GetType().Name);
        }

        span?.SetTag("ticket.id", ticket.Id);
        span?.SetTag("ticket.status", ticket.Status);
        span?.SetTag("ticket.priority", ticket.Priority);
        if (ticket.Status != "extracted")
        {
            span?.SetStatus(ActivityStatusCode.Error, ticket.UnextractedReason);
        }

        // Ids and outcomes only. What the customer wrote is in the store, not in the logs.
        log.LogInformation("Stored {TicketId}: {Status}, {Priority}, for a person: {ForAPerson}", ticket.Id, ticket.Status, ticket.Priority, ticket.ForAPerson);
        return ticket;
    }

    private StoredTicket Unextracted(string email, DateTimeOffset received, string reason, int attempts) =>
        store.Add(id => new StoredTicket(id, received, "unextracted", null, reason, "normal", Rules.DueAt(received, "normal"), true, null, attempts) { Email = email });
}
