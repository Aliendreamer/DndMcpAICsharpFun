using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface ITocMapExtractor
{
    Task<IReadOnlyList<TocSectionEntry>> ExtractMapAsync(
        string tocPageText,
        CancellationToken ct = default);
}
