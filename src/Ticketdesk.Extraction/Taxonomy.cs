namespace Ticketdesk.Extraction;

/// <summary>The values each labelled field may take. The prompt, the schema and the dataset check read these.</summary>
public static class Taxonomy
{
    public static readonly IReadOnlyList<string> Categories = ["outage", "bug", "billing", "account", "question", "feature_request"];
    public static readonly IReadOnlyList<string> Plans = ["starter", "team", "enterprise", "unknown"];
    public static readonly IReadOnlyList<string> ProductAreas = ["api", "dashboard", "billing", "integrations", "mobile", "other"];
    public static readonly IReadOnlyList<string> Sentiments = ["positive", "neutral", "frustrated", "angry"];
    public const int MaxSummaryLength = 200;
}
