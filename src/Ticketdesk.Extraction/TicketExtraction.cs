namespace Ticketdesk.Extraction;

using System.Text.Json.Serialization;

/// <summary>
/// What a support desk needs to know about an inbound email before a person reads it. The first three
/// fields are exactly what the desk's priority rules take: whether there is an outage, the customer's
/// plan, and the category.
/// </summary>
public sealed record TicketExtraction(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("outage")] bool Outage,
    [property: JsonPropertyName("plan")] string Plan,
    [property: JsonPropertyName("product_area")] string ProductArea,
    [property: JsonPropertyName("sentiment")] string Sentiment,
    [property: JsonPropertyName("needs_human")] bool NeedsHuman,
    [property: JsonPropertyName("summary")] string Summary);
