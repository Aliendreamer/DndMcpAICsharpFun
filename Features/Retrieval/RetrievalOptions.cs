namespace DndMcpAICsharpFun.Features.Retrieval;

public sealed class RetrievalOptions
{
    public float ScoreThreshold { get; set; } = 0.5f;
    public int MaxTopK { get; set; } = 20;
}
