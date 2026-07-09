namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class GroundingOptions
{
    public double SimilarityFloor { get; set; } = 0.5;
    public int PageWindow { get; set; } = 2;
}
