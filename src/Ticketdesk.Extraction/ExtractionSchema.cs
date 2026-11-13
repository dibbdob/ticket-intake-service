namespace Ticketdesk.Extraction;

using System.Text.Json;

/// <summary>
/// The JSON schema the API enforces on the reply (structured outputs). The prompt says what each field
/// means; the schema says what shape the reply must have, so the extractor never has to recover JSON from
/// prose or a code fence.
/// </summary>
public static class ExtractionSchema
{
    public static IReadOnlyDictionary<string, JsonElement> Build()
    {
        var properties = new Dictionary<string, object>
        {
            ["category"] = new { type = "string", @enum = Taxonomy.Categories },
            ["outage"] = new { type = "boolean" },
            ["plan"] = new { type = "string", @enum = Taxonomy.Plans },
            ["product_area"] = new { type = "string", @enum = Taxonomy.ProductAreas },
            ["sentiment"] = new { type = "string", @enum = Taxonomy.Sentiments },
            ["needs_human"] = new { type = "boolean" },
            // No maxLength: string length is not a constraint the schema can carry here, so the length
            // rule is checked after the reply arrives.
            ["summary"] = new { type = "string" },
        };

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(properties),
            ["required"] = JsonSerializer.SerializeToElement(properties.Keys),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        };
    }
}
