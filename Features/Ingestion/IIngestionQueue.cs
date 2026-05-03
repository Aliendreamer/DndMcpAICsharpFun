namespace DndMcpAICsharpFun.Features.Ingestion;

public enum IngestionWorkType { IngestBlocks, IngestEntities }

public record IngestionWorkItem(IngestionWorkType Type, int BookId);

public interface IIngestionQueue
{
    bool TryEnqueue(IngestionWorkItem item);
}
