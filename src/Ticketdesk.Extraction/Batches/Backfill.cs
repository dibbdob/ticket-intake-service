namespace Ticketdesk.Extraction.Batches;

using System.Text.RegularExpressions;

/// <summary>What a backfill produced: the extractions, and the emails that need another pass, with why.</summary>
public sealed record BackfillResult(
    IReadOnlyDictionary<string, TicketExtraction> Extracted,
    IReadOnlyDictionary<string, FailureKind> NeedsAnotherPass,
    TokenUsage Usage);

/// <summary>
/// Extracts a backlog of emails through the Message Batches API, which bills at half the live rate and
/// returns within a day, usually much sooner. For work nobody is waiting on.
///
/// Results come back in any order, so each is matched to its email by custom_id, never by position. A
/// batch request has no second turn, so a reply that breaks a rule is not corrected here: it is listed
/// for another pass through the live extractor, which can correct it.
/// </summary>
public sealed partial class Backfill(IBatchGateway gateway, string model, TimeSpan pollEvery)
{
    public async Task<BackfillResult> Run(IReadOnlyList<(string Id, string Email)> emails, CancellationToken cancellationToken = default)
    {
        Validate(emails);

        var batchId = await gateway.Submit(
            emails.Select(e => (e.Id, TicketExtractor.RequestFor(model, e.Email))).ToList(), cancellationToken);

        while (!await gateway.HasEnded(batchId, cancellationToken))
        {
            await Task.Delay(pollEvery, cancellationToken);
        }

        var extracted = new Dictionary<string, TicketExtraction>();
        var again = new Dictionary<string, FailureKind>();
        var usage = new TokenUsage(0, 0);
        await foreach (var item in gateway.Results(batchId, cancellationToken))
        {
            if (item.Reply is null)
            {
                again[item.CustomId] = FailureKind.NotRun;
                continue;
            }

            usage += item.Reply.Usage;
            try
            {
                extracted[item.CustomId] = TicketExtractor.Accept(item.Reply);
            }
            catch (ExtractionFailedException failure)
            {
                again[item.CustomId] = failure.Kind;
            }
        }

        // An email the batch never reported on is not silently dropped.
        foreach (var (id, _) in emails.Where(e => !extracted.ContainsKey(e.Id) && !again.ContainsKey(e.Id)))
        {
            again[id] = FailureKind.NotRun;
        }

        return new BackfillResult(extracted, again, usage);
    }

    private static void Validate(IReadOnlyList<(string Id, string Email)> emails)
    {
        var bad = emails.Where(e => !CustomId().IsMatch(e.Id)).Select(e => e.Id).ToList();
        if (bad.Count > 0)
        {
            throw new ArgumentException($"A custom_id is 1 to 64 letters, digits, '-' or '_': {string.Join(", ", bad)}", nameof(emails));
        }

        var duplicates = emails.GroupBy(e => e.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            throw new ArgumentException($"Each email needs its own custom_id; repeated: {string.Join(", ", duplicates)}", nameof(emails));
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex CustomId();
}
