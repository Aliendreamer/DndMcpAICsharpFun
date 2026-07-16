namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// The result of extracting a PDF: the ordered prose blocks, plus the section-header
/// structure items from the same single conversion. Consumers use the headings to
/// build a fallback table of contents when the PDF has no embedded bookmarks, without
/// re-converting the file.
/// </summary>
public sealed record PdfExtraction(
    IReadOnlyList<PdfBlock> Blocks,
    IReadOnlyList<PdfStructureItem> Headings);
