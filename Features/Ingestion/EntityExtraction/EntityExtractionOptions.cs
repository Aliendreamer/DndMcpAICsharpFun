using System.ComponentModel.DataAnnotations;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityExtractionOptions
{
    [Required]
    public string CanonicalDirectory { get; set; } = "data/canonical";
    [Required]
    public string SchemasDirectory { get; set; } = "Schemas/canonical";
    public int MaxRetriesPerEntity { get; set; } = 3;
    public int MaxOutputTokensPerEntity { get; set; } = 8192;
    public int ProgressLogIntervalSeconds { get; set; } = 60;
    public int CheckpointIntervalCandidates { get; set; } = 100;
    public string ConversionCacheDirectory { get; set; } = "data/conversion-cache";
    public string ExamplesDirectory { get; set; } = "Schemas/examples";
    public int MaxTokensPerChunk { get; set; } = 2000;
}
