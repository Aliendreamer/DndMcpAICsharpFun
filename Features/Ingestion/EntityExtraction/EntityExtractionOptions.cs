using System.ComponentModel.DataAnnotations;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityExtractionOptions
{
    [Required]
    public string CanonicalDirectory { get; set; } = "books/canonical";
    [Required]
    public string SchemasDirectory { get; set; } = "Schemas/canonical";
    public int MaxRetriesPerEntity { get; set; } = 3;
    public int MaxOutputTokensPerEntity { get; set; } = 8192;
    public int ProgressLogIntervalSeconds { get; set; } = 60;
    public int CheckpointIntervalCandidates { get; set; } = 100;
    public string ConversionCacheDirectory { get; set; } = "books/conversion-cache";
    public string ExamplesDirectory { get; set; } = "Schemas/examples";
    public int MaxTokensPerChunk { get; set; } = 2000;

    /// <summary>
    /// Max characters of a candidate sent to the content-first union/type-decision call. The model
    /// only needs the top of a candidate to identify its type (and a stat block sits near the top);
    /// without this cap, a huge section (e.g. a full PHB class) makes the union call multi-minute and
    /// can fail. Field extraction still chunks the FULL text, so nothing is lost.
    /// </summary>
    public int MaxTypeDecisionChars { get; set; } = 8000;
}
