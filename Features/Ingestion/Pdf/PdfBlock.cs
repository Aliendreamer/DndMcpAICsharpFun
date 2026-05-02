using UglyToad.PdfPig.Core;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed record PdfBlock(string Text, int PageNumber, int Order, PdfRectangle BoundingBox);
