namespace Ticketdesk.Extraction.Batches;

/// <summary>One result from a batch: the id it was submitted under, and its reply or why there is none.</summary>
public sealed record BatchItem(string CustomId, ModelReply? Reply, string? Error);

/// <summary>The Message Batches API, in the extractor's terms. Tests replace it.</summary>
public interface IBatchGateway
{
    Task<string> Submit(IReadOnlyList<(string CustomId, ModelRequest Request)> requests, CancellationToken cancellationToken);

    Task<bool> HasEnded(string batchId, CancellationToken cancellationToken);

    IAsyncEnumerable<BatchItem> Results(string batchId, CancellationToken cancellationToken);
}
