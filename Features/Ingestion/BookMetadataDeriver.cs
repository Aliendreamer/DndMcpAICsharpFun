using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

namespace DndMcpAICsharpFun.Features.Ingestion;

/// <summary>Single source of truth for a book's canonical SourceBook / Edition (shared by the LLM and programmatic pipelines).</summary>
public static class BookMetadataDeriver
{
    public static (string SourceBook, string Edition) Derive(IngestionRecord record, BookSourceRegistry registry)
    {
        var sourceBook = record.FivetoolsSourceKey ?? record.DisplayName;
        var edition = record.FivetoolsSourceKey is { } key && registry.TryGetBook(key) is { } info
            ? (info.PublishedYear >= 2024 ? "Edition2024" : "Edition2014")
            : record.Version;
        return (sourceBook, edition);
    }
}
