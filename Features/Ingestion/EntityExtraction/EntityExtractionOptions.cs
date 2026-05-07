namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityExtractionOptions
{
    public string CanonicalDirectory { get; set; } = "data/canonical";
    public string SchemasDirectory { get; set; } = "Schemas/canonical";
    public int MaxRetriesPerEntity { get; set; } = 3;
    public int MaxOutputTokensPerEntity { get; set; } = 8192;
    public int ProgressLogIntervalSeconds { get; set; } = 60;
    public int CheckpointIntervalCandidates { get; set; } = 100;
    public string DoclingCacheDirectory { get; set; } = "data/docling-cache";
}
