namespace Ticketdesk.Extraction.Evaluation;

/// <summary>Price per million tokens for one model.</summary>
public sealed record ModelPrice(decimal Input, decimal Output)
{
    /// <summary>A cache read costs a tenth of an uncached input token.</summary>
    public decimal CacheRead => this.Input / 10m;

    /// <summary>Writing the five-minute cache costs a quarter more than an uncached input token.</summary>
    public decimal CacheWrite => this.Input * 1.25m;

    public decimal Cost(TokenUsage usage) =>
        ((usage.Input * this.Input) + (usage.Output * this.Output) +
         (usage.CacheRead * this.CacheRead) + (usage.CacheWrite * this.CacheWrite)) / 1_000_000m;
}

/// <summary>
/// First-party API list prices in US dollars, as published on 2026-11-03. Prices change: check the pricing
/// page before reading a cost in a report as current. Batch requests are billed at half these rates.
/// </summary>
public static class Prices
{
    public const string AsOf = "2026-11-03";

    public static readonly IReadOnlyDictionary<string, ModelPrice> PerMillionTokens = new Dictionary<string, ModelPrice>
    {
        ["claude-opus-5-5"] = new(4.00m, 20.00m),
        ["claude-sonnet-5-5"] = new(2.00m, 10.00m),
        ["claude-haiku-5-5"] = new(0.10m, 0.50m),

        // Models a server-side fallback can answer from. A run must be able to price whoever answered.
        ["claude-fable-5-2"] = new(8.00m, 40.00m),
        ["claude-opus-5"] = new(5.00m, 25.00m),
        ["claude-opus-4-8"] = new(5.00m, 25.00m),
        ["claude-sonnet-5"] = new(2.00m, 10.00m),
    };

    public static decimal Cost(string model, TokenUsage usage) =>
        PerMillionTokens.TryGetValue(model, out var price)
            ? price.Cost(usage)
            : throw new KeyNotFoundException($"No price for {model}; add it to Prices before reporting its cost.");
}
