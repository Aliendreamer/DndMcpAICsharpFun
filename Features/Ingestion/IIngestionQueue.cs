namespace DndMcpAICsharpFun.Features.Ingestion;

public enum IngestionWorkType { Extract, IngestJson }

public record IngestionWorkItem(IngestionWorkType Type, int BookId);

public interface IIngestionQueue
{
    bool TryEnqueue(IngestionWorkItem item);
}
