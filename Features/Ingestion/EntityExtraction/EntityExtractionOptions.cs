namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityExtractionOptions
{
    public string CanonicalDirectory { get; set; } = "data/canonical";
    public string SchemasDirectory { get; set; } = "Schemas/canonical";
    public int MaxRetriesPerEntity { get; set; } = 3;
    public int ProgressLogIntervalSeconds { get; set; } = 60;
}
