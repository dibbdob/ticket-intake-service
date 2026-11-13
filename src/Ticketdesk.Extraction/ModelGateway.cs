namespace Ticketdesk.Extraction;

using System.Text.Json;

/// <summary>One turn of a conversation: who said it, and what.</summary>
public sealed record Turn(string Role, string Text)
{
    public static Turn User(string text) => new("user", text);

    public static Turn Assistant(string text) => new("assistant", text);
}

/// <summary>One request to a model, in the extractor's terms rather than the SDK's.</summary>
public sealed record ModelRequest(
    string Model,
    string System,
    IReadOnlyList<Turn> Turns,
    IReadOnlyDictionary<string, JsonElement>? Schema = null,
    bool CacheSystem = false);

/// <summary>Token counts for one call, as the API reports them.</summary>
public sealed record TokenUsage(long Input, long Output, long CacheRead = 0, long CacheWrite = 0)
{
    public static TokenUsage operator +(TokenUsage a, TokenUsage b) =>
        new(a.Input + b.Input, a.Output + b.Output, a.CacheRead + b.CacheRead, a.CacheWrite + b.CacheWrite);
}

/// <summary>Tokens one model used. A routed extraction can spend on more than one, at different prices.</summary>
public sealed record ModelSpend(string Model, TokenUsage Usage);

/// <summary>What came back: the text, why generation stopped, which model answered, and what it used.</summary>
public sealed record ModelReply(string Text, string StopReason, TokenUsage Usage, string Model);

/// <summary>
/// The one place the extractor reaches a model. The tests replace it, so the build never calls the API
/// and never costs anything.
/// </summary>
public interface IModelGateway
{
    Task<ModelReply> Complete(ModelRequest request, CancellationToken cancellationToken);
}
