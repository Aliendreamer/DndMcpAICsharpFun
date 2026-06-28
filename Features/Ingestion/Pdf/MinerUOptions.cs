namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// Spike configuration for the MinerU-based PDF structure converter. When <see cref="Enabled"/>
/// is true, <see cref="MinerUPdfConverter"/> replaces the Marker converter as the registered
/// <see cref="IPdfStructureConverter"/>, reading pre-produced MinerU output from
/// <see cref="OutputDirectory"/>.
/// </summary>
public sealed class MinerUOptions
{
    /// <summary>Use MinerU instead of Marker for PDF structure conversion.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Directory holding MinerU CLI output, laid out as
    /// <c>&lt;OutputDirectory&gt;/&lt;pdf-stem&gt;/&lt;method&gt;/&lt;pdf-stem&gt;_content_list.json</c>.
    /// </summary>
    public string OutputDirectory { get; set; } = "/mineru-out";
}
