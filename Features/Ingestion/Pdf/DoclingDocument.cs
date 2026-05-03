namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// Our internal representation of a Docling conversion result. The
/// <see cref="DoclingPdfConverter"/> deserialises docling-serve's JSON
/// response into this simplified shape.
/// </summary>
public sealed record DoclingDocument(string Markdown, IReadOnlyList<DoclingItem> Items);

public sealed record DoclingItem(string Type, string Text, int PageNumber, int? Level);
