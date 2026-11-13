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
        var fallback = SupportsServerFallback(request.Model);
        var response = await client.Beta.Messages.Create(
            new MessageCreateParams
            {
                Model = request.Model,
                MaxTokens = 2048,
                // A cache breakpoint on the system prompt: the schema and system prompt are the stable
                // prefix, the email is not. A prefix shorter than the model's minimum silently does not
                // cache, so the eval reports cache-read tokens rather than assuming a hit.
                System = new List<BetaTextBlockParam>
                {
                    new() { Text = request.System, CacheControl = request.CacheSystem ? new BetaCacheControlEphemeral() : null },
                },
                Messages = request.Turns.Select(ToParam).ToList(),
                // Classification does well at low effort; Claude Opus 5.5 defaults to medium.
                OutputConfig = new()
                {
                    Effort = Effort.Low,
                    Format = request.Schema is { } schema ? new() { Schema = schema.ToDictionary() } : null,
                },
                Betas = fallback ? [FallbackBeta] : null,
                Fallbacks = fallback ? (BetaFallbacksParam)new Default() : null,
            },
            cancellationToken: cancellationToken);

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        var usage = new TokenUsage(
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.Usage.CacheReadInputTokens ?? 0,
            response.Usage.CacheCreationInputTokens ?? 0);
        return new ModelReply(text, response.StopReason?.Raw() ?? "unknown", usage, response.Model.Raw());
    }

    public static bool SupportsServerFallback(string model) =>
        model is "claude-opus-5-5" or "claude-sonnet-5-5" or "claude-fable-5-2";

    private static BetaMessageParam ToParam(Turn turn) => turn.Role switch
    {
        "user" => new BetaUserMessageParam { Content = turn.Text },
        "assistant" => new BetaAssistantMessageParam { Content = turn.Text },
        _ => throw new ArgumentOutOfRangeException(nameof(turn), turn.Role, "A turn is user or assistant."),
    };
}
