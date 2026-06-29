namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// Configuration for the MinerU-based PDF structure converter. <see cref="MinerUPdfConverter"/>
/// POSTs each PDF to the live MinerU service at <see cref="ServiceUrl"/> and maps the returned
/// <c>content_list</c> blocks onto a <see cref="PdfStructureDocument"/>.
/// </summary>
public sealed class MinerUOptions
{
    /// <summary>Base URL of the MinerU HTTP service (e.g. <c>http://mineru:8000</c>).</summary>
    public string ServiceUrl { get; set; } = "http://mineru:8000";

    /// <summary>MinerU parsing backend (e.g. <c>pipeline</c>).</summary>
    public string Backend { get; set; } = "pipeline";

    /// <summary>Parse method passed to MinerU (e.g. <c>ocr</c>).</summary>
    public string Method { get; set; } = "ocr";

    /// <summary>HTTP timeout, in minutes, for a single conversion request (OCR is slow on large books).</summary>
    public int ConversionTimeoutMinutes { get; set; } = 120;
}
