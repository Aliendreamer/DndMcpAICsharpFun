namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// Our internal representation of a PDF structure conversion result. The
/// <see cref="MinerUPdfConverter"/> maps the MinerU service's <c>content_list</c>
/// response into this simplified shape.
/// </summary>
public sealed record PdfStructureDocument(string Markdown, IReadOnlyList<PdfStructureItem> Items);

public sealed record PdfStructureItem(string Type, string Text, int PageNumber, int? Level);