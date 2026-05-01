using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface ITocCategoryClassifier
{
    Task<TocCategoryMap> ClassifyAsync(
        IReadOnlyList<PdfBookmark> bookmarks,
        CancellationToken ct = default);
}
