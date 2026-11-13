namespace Ticketdesk.Extraction.Evaluation;

/// <summary>What happened to one ticket.</summary>
public sealed record TicketOutcome(
    string Id,
    Labels Expected,
    Labels? Actual,
    FailureKind? Failure,
    int Attempts,
    TokenUsage Usage,
    string AnsweredBy,
    decimal Cost)
{
    public bool Correct(string field) => this.Actual is { } actual && actual.Get(field) == this.Expected.Get(field);

    public bool AllCorrect => Labels.Fields.All(this.Correct);
}

/// <summary>A field's accuracy with a 95% Wilson interval, which stays honest at small sample sizes.</summary>
public sealed record Accuracy(int Correct, int Total)
{
    public double Rate => this.Total == 0 ? 0 : (double)this.Correct / this.Total;

    public (double Low, double High) Interval
    {
        get
        {
            if (this.Total == 0)
            {
                return (0, 1);
            }

            const double z = 1.96;
            double n = this.Total, p = this.Rate, z2 = z * z;
            var centre = (p + (z2 / (2 * n))) / (1 + (z2 / n));
            var half = z * Math.Sqrt((p * (1 - p) / n) + (z2 / (4 * n * n))) / (1 + (z2 / n));
            return (Math.Max(0, centre - half), Math.Min(1, centre + half));
        }
    }
}

/// <summary>The whole run: every outcome, and the totals a report needs.</summary>
public sealed record EvalRun(string Label, string Model, DateTimeOffset StartedAt, IReadOnlyList<TicketOutcome> Outcomes)
{
    public Accuracy For(string field) => new(this.Outcomes.Count(o => o.Correct(field)), this.Outcomes.Count);

    public Accuracy AllFields => new(this.Outcomes.Count(o => o.AllCorrect), this.Outcomes.Count);

    public TokenUsage Usage => this.Outcomes.Aggregate(new TokenUsage(0, 0), (sum, o) => sum + o.Usage);

    public decimal Cost => this.Outcomes.Sum(o => o.Cost);

    public int Failures => this.Outcomes.Count(o => o.Failure is not null);

    public int Retried => this.Outcomes.Count(o => o.Attempts > 1);

    /// <summary>Tickets whose extraction was answered by a model other than the one asked: a fallback.</summary>
    public int AnsweredByFallback => this.Outcomes.Count(o => o.Failure is null && o.AnsweredBy != this.Model);
}
