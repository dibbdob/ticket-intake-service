namespace Ticketdesk.Extraction;

using Anthropic;
using Anthropic.Models.Beta.Messages;

/// <summary>
/// The gateway to the Claude API. The client reads its credentials from the environment
/// (ANTHROPIC_API_KEY, or an `ant auth login` profile); nothing here holds a key.
///
/// It uses the beta Messages endpoint for one reason: server-side refusal fallback. When a safety
/// classifier declines a request on a model that supports fallback, the API re-runs it on a fallback model
/// inside the same call, and the reply names the model that answered. Haiku has no server-side fallback,
/// so a request to it is sent without one.
/// </summary>
public sealed class AnthropicGateway(AnthropicClient client) : IModelGateway
{
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    public async Task<ModelReply> Complete(ModelRequest request, CancellationToken cancellationToken)
    {
        var response = await client.Beta.Messages.Create(ToParams(request), cancellationToken: cancellationToken);

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        var usage = new TokenUsage(
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.Usage.CacheReadInputTokens ?? 0,
            response.Usage.CacheCreationInputTokens ?? 0);
        return new ModelReply(text, response.StopReason?.Raw() ?? "unknown", usage, response.Model.Raw());
    }

    /// <summary>
    /// The request as it will be sent. A field the SDK is given, even as null, goes on the wire, and the API
    /// refuses `fallbacks` on a model without server-side fallback; so the fallback fields are set only for
    /// the models that have it.
    /// </summary>
    public static MessageCreateParams ToParams(ModelRequest request)
    {
        var common = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = 2048,
            // A cache breakpoint on the system prompt: the schema and system prompt are the stable prefix,
            // the email is not. A prefix shorter than the model's minimum silently does not cache, so the
            // eval reports cache-read tokens rather than assuming a hit.
            System = new List<BetaTextBlockParam> { SystemBlock(request) },
            Messages = request.Turns.Select(ToParam).ToList(),
            // Classification does well at low effort; Claude Opus 5.5 defaults to medium.
            OutputConfig = request.Schema is { } schema
                ? new() { Effort = Effort.Low, Format = new() { Schema = schema.ToDictionary() } }
                : new() { Effort = Effort.Low },
        };
        return SupportsServerFallback(request.Model)
            ? common with { Betas = [FallbackBeta], Fallbacks = new Default() }
            : common;
    }

    private static BetaTextBlockParam SystemBlock(ModelRequest request) => request.CacheSystem
        ? new() { Text = request.System, CacheControl = new BetaCacheControlEphemeral() }
        : new() { Text = request.System };

    public static bool SupportsServerFallback(string model) =>
        model is "claude-opus-5-5" or "claude-sonnet-5-5" or "claude-fable-5-2";

    private static BetaMessageParam ToParam(Turn turn) => turn.Role switch
    {
        "user" => new BetaUserMessageParam { Content = turn.Text },
        "assistant" => new BetaAssistantMessageParam { Content = turn.Text },
        _ => throw new ArgumentOutOfRangeException(nameof(turn), turn.Role, "A turn is user or assistant."),
    };
}
