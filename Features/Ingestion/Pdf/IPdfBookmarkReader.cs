namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public interface IPdfBookmarkReader
{
    IReadOnlyList<PdfBookmark> ReadBookmarks(string filePath);
}
