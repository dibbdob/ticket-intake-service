namespace Ticketdesk.Extraction.Evaluation;

using System.Globalization;
using System.Text;

/// <summary>A run as Markdown: what was asked, how it scored, what it cost, and what it got wrong.</summary>
public static class EvalReport
{
    public static string Markdown(EvalRun run)
    {
        var c = CultureInfo.InvariantCulture;
        var md = new StringBuilder();
        md.AppendLine(c, $"# Eval: {run.Label}").AppendLine();
        md.AppendLine(c, $"- Model asked: `{run.Model}`");
        md.AppendLine(c, $"- Run: {run.StartedAt:yyyy-MM-dd HH:mm} UTC, {run.Outcomes.Count} tickets");
        md.AppendLine(c, $"- Failures: {run.Failures}; retried: {run.Retried}; answered by a fallback model: {run.AnsweredByFallback}");
        var u = run.Usage;
        md.AppendLine(c, $"- Tokens: {u.Input:N0} in, {u.Output:N0} out, {u.CacheRead:N0} cache read, {u.CacheWrite:N0} cache write");
        md.AppendLine(c, $"- Cost: ${run.Cost:0.0000} at list prices as of {Prices.AsOf}; ${run.Cost / Math.Max(1, run.Outcomes.Count):0.000000} per ticket");
        var allCorrect = run.AllFields.Correct;
        md.AppendLine(allCorrect > 0
            ? string.Create(c, $"- Cost per ticket with all six fields right: ${run.Cost / allCorrect:0.000000} (the number to compare configurations on)")
            : "- Cost per ticket with all six fields right: none were");
        md.AppendLine();

        md.AppendLine("| Field | Correct | Accuracy | 95% interval |").AppendLine("|---|---|---|---|");
        foreach (var field in Labels.Fields)
        {
            Row(md, field, run.For(field));
        }

        Row(md, "**all six**", run.AllFields);
        md.AppendLine();
        md.AppendLine(c, $"With {run.Outcomes.Count} tickets an interval this wide is expected; two configurations whose intervals overlap have not been shown to differ.");
        md.AppendLine();

        var wrong = run.Outcomes.Where(o => !o.AllCorrect).ToList();
        if (wrong.Count > 0)
        {
            md.AppendLine("## Misses").AppendLine().AppendLine("| Ticket | Field | Expected | Got |").AppendLine("|---|---|---|---|");
            foreach (var o in wrong)
            {
                if (o.Actual is null)
                {
                    md.AppendLine(c, $"| {o.Id} | (failed) | | {o.Failure} |");
                    continue;
                }

                foreach (var field in Labels.Fields.Where(f => !o.Correct(f)))
                {
                    md.AppendLine(c, $"| {o.Id} | {field} | {o.Expected.Get(field)} | {o.Actual.Get(field)} |");
                }
            }
        }

        return md.ToString();
    }

    private static void Row(StringBuilder md, string field, Accuracy a)
    {
        var (low, high) = a.Interval;
        md.AppendLine(CultureInfo.InvariantCulture, $"| {field} | {a.Correct}/{a.Total} | {a.Rate:P0} | {low:P0} to {high:P0} |");
    }
}
