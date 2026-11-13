namespace Ticketdesk.Extraction.Batches;

using System.Runtime.CompilerServices;
using Anthropic;
using Anthropic.Models.Messages;
using Anthropic.Models.Messages.Batches;

/// <summary>
/// The Message Batches API. It takes the same request shape as a live call, with one difference that
/// matters here: server-side refusal fallback is not accepted on batches, so none is sent. A refusal in a
/// batch comes back as a refusal and goes to the live pass.
/// </summary>
public sealed class AnthropicBatchGateway(AnthropicClient client) : IBatchGateway
{
    public async Task<string> Submit(IReadOnlyList<(string CustomId, ModelRequest Request)> requests, CancellationToken cancellationToken)
    {
        var batch = await client.Messages.Batches.Create(
            new BatchCreateParams
            {
                Requests = requests.Select(r => new Request { CustomID = r.CustomId, Params = ToParams(r.Request) }).ToList(),
            },
            cancellationToken: cancellationToken);
        return batch.ID;
    }

    public async Task<bool> HasEnded(string batchId, CancellationToken cancellationToken)
    {
        var batch = await client.Messages.Batches.Retrieve(batchId, cancellationToken: cancellationToken);
        return batch.ProcessingStatus.Raw() == "ended";
    }

    public async IAsyncEnumerable<BatchItem> Results(string batchId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in client.Messages.Batches.ResultsStreaming(batchId, cancellationToken: cancellationToken))
        {
            if (item.Result.TryPickSucceeded(out var succeeded))
            {
                var m = succeeded.Message;
                var text = string.Concat(m.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
                var usage = new TokenUsage(m.Usage.InputTokens, m.Usage.OutputTokens, m.Usage.CacheReadInputTokens ?? 0, m.Usage.CacheCreationInputTokens ?? 0);
                yield return new BatchItem(item.CustomID, new ModelReply(text, m.StopReason?.Raw() ?? "unknown", usage, m.Model.Raw()), null);
            }
            else
            {
                yield return new BatchItem(item.CustomID, null, item.Result.ToString());
            }
        }
    }

    private static Params ToParams(ModelRequest request) => new()
    {
        Model = request.Model,
        MaxTokens = 2048,
        System = new List<TextBlockParam>
        {
            new() { Text = request.System, CacheControl = request.CacheSystem ? new CacheControlEphemeral() : null },
        },
        Messages = request.Turns.Select(t => t.Role == "user"
            ? (MessageParam)new UserMessageParam { Content = t.Text }
            : new AssistantMessageParam { Content = t.Text }).ToList(),
        OutputConfig = new()
        {
            Effort = Effort.Low,
            Format = request.Schema is { } schema ? new JsonOutputFormat { Schema = schema.ToDictionary() } : null,
        },
    };
}
