namespace DndMcpAICsharpFun.Features.Ingestion;

public enum IngestionWorkType { IngestBlocks, IngestEntities, ExtractEntities }

public record IngestionWorkItem(IngestionWorkType Type, int BookId, bool Force = false, bool ErrorsOnly = false);

public interface IIngestionQueue
{
    /// <summary>
    /// Enqueues <paramref name="item"/> for background processing. Returns <c>false</c> (without
    /// enqueueing) when the same <see cref="IngestionWorkItem.BookId"/> is already enqueued or
    /// currently being processed — callers should treat this as a duplicate-request conflict.
    /// </summary>
    bool TryEnqueue(IngestionWorkItem item);
}