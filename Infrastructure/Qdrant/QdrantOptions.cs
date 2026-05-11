namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

public sealed class QdrantOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
    public int VectorSize { get; set; } = 768;
    public string BlocksCollectionName { get; set; } = "dnd_blocks";
    public string EntitiesCollectionName { get; set; } = "dnd_entities";
    public float HybridAlpha { get; set; } = 0.5f;
}
