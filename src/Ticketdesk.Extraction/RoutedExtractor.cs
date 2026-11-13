namespace Ticketdesk.Extraction;

/// <summary>
/// Try a cheap model first and escalate to a stronger one only when the cheap one struggles: when it
/// fails, or when it needed a correction to get there. A correction is taken as a sign the email is hard,
/// and a hard email is where the stronger model's price is worth paying.
///
/// This is a hypothesis, not a default (ADR 0006). The eval must show the route beats both models on
/// their own, at cost per correct extraction, before it is used.
/// </summary>
public sealed class RoutedExtractor(IExtractor first, IExtractor escalation) : IExtractor
{
    public async Task<ExtractionResult> Extract(string email, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModelSpend> spent;
        try
        {
            var cheap = await first.Extract(email, cancellationToken);
            if (cheap.Attempts == 1)
            {
                return cheap;
            }

            spent = cheap.Spend;
        }
        catch (ExtractionFailedException failure)
        {
            spent = failure.Spend;
        }

        try
        {
            var strong = await escalation.Extract(email, cancellationToken);
            IReadOnlyList<ModelSpend> all = [.. spent, .. strong.Spend];
            return strong with
            {
                Attempts = strong.Attempts + 1,
                Usage = all.Aggregate(new TokenUsage(0, 0), (sum, s) => sum + s.Usage),
                Spend = all,
            };
        }
        catch (ExtractionFailedException failure)
        {
            throw new ExtractionFailedException(failure.Kind, failure.Message, failure.Reply, failure.Problems, failure.Usage, failure.Attempts + 1)
            {
                Spend = [.. spent, .. failure.Spend],
            };
        }
    }
}
