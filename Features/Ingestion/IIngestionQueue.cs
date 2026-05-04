namespace DndMcpAICsharpFun.Features.Ingestion;

public enum IngestionWorkType { IngestBlocks, IngestEntities, ExtractEntities }

public record IngestionWorkItem(IngestionWorkType Type, int BookId, bool Force = false);

public interface IIngestionQueue
{
    bool TryEnqueue(IngestionWorkItem item);
}
