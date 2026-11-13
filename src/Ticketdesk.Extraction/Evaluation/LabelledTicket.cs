namespace Ticketdesk.Extraction.Evaluation;

using System.Text.Json;

/// <summary>One email and the extraction a careful person would make from it.</summary>
public sealed record LabelledTicket(string Id, string Text, Labels Expected)
{
    public static IReadOnlyList<LabelledTicket> Load(string path) =>
        File.ReadLines(path).Where(l => l.Length > 0).Select(Parse).ToList();

    private static LabelledTicket Parse(string line)
    {
        var row = JsonDocument.Parse(line).RootElement;
        var e = row.GetProperty("expected");
        return new LabelledTicket(
            row.GetProperty("id").GetString()!,
            row.GetProperty("text").GetString()!,
            new Labels(
                e.GetProperty("category").GetString()!,
                e.GetProperty("outage").GetBoolean(),
                e.GetProperty("plan").GetString()!,
                e.GetProperty("product_area").GetString()!,
                e.GetProperty("sentiment").GetString()!,
                e.GetProperty("needs_human").GetBoolean()));
    }
}

/// <summary>The scored fields. The summary is not scored: its wording has no single right answer.</summary>
public sealed record Labels(string Category, bool Outage, string Plan, string ProductArea, string Sentiment, bool NeedsHuman)
{
    public static readonly IReadOnlyList<string> Fields = ["category", "outage", "plan", "product_area", "sentiment", "needs_human"];

    public static Labels From(TicketExtraction e) => new(e.Category, e.Outage, e.Plan, e.ProductArea, e.Sentiment, e.NeedsHuman);

    public string Get(string field) => field switch
    {
        "category" => this.Category,
        "outage" => this.Outage ? "true" : "false",
        "plan" => this.Plan,
        "product_area" => this.ProductArea,
        "sentiment" => this.Sentiment,
        "needs_human" => this.NeedsHuman ? "true" : "false",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };
}
