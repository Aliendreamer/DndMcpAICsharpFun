using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed class JsonIngestionPipeline(
    IEntityJsonStore jsonStore,
    IEmbeddingIngestor embeddingIngestor) : IJsonIngestionPipeline
{
    public async Task IngestAsync(int bookId, string fileHash, CancellationToken ct = default)
    {
        await jsonStore.RunMergePassAsync(bookId, ct);

        var pages = await jsonStore.LoadAllPagesAsync(bookId, ct);

        var chunks = new List<ContentChunk>();
        int chunkIndex = 0;
        foreach (var page in pages)
        {
            foreach (var entity in page.Entities)
            {
                var description = entity.Data["description"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(description)) continue;

                if (!Enum.TryParse<ContentCategory>(entity.Type, ignoreCase: true, out var category))
                    category = ContentCategory.Rule;

                if (!Enum.TryParse<DndVersion>(entity.Version, ignoreCase: true, out var version))
                    version = DndVersion.Edition2014;

                var metadata = new ChunkMetadata(
                    SourceBook:  entity.SourceBook,
                    Version:     version,
                    Category:    category,
                    EntityName:  entity.Name,
                    Chapter:     string.Empty,
                    PageNumber:  entity.Page,
                    ChunkIndex:  chunkIndex++);

                chunks.Add(new ContentChunk(description, metadata));
            }
        }

        await embeddingIngestor.IngestAsync(chunks, fileHash, ct);
    }
}
