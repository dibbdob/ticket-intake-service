namespace Ticketdesk.Extraction;

/// <summary>
/// What a schema-valid extraction must also satisfy. The schema guarantees shape; these are the rules the
/// schema cannot express. Each returns a sentence the model can act on when it is asked to correct itself.
/// </summary>
public static class ExtractionRules
{
    public static IReadOnlyList<string> Check(TicketExtraction e)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(e.Summary))
        {
            problems.Add("summary is empty; write one sentence saying what the customer needs.");
        }
        else if (e.Summary.Length > Taxonomy.MaxSummaryLength)
        {
            problems.Add($"summary is {e.Summary.Length} characters; it must be at most {Taxonomy.MaxSummaryLength}.");
        }

        // The converse does not hold: a suspended account is an outage for the customer without being an
        // outage of the product, so outage can be true under billing or account.
        if (e.Category == "outage" && !e.Outage)
        {
            problems.Add("category is outage but outage is false; an outage category means the customer cannot use the product.");
        }

        // The schema enforces these, but a gateway sent without one must not let a stray value through.
        Allowed("category", e.Category, Taxonomy.Categories, problems);
        Allowed("plan", e.Plan, Taxonomy.Plans, problems);
        Allowed("product_area", e.ProductArea, Taxonomy.ProductAreas, problems);
        Allowed("sentiment", e.Sentiment, Taxonomy.Sentiments, problems);
        return problems;
    }

    private static void Allowed(string field, string value, IReadOnlyList<string> allowed, List<string> problems)
    {
        if (!allowed.Contains(value))
        {
            problems.Add($"{field} '{value}' is not one of: {string.Join(", ", allowed)}.");
        }
    }
}
