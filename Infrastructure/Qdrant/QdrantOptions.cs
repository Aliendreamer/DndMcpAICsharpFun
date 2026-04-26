namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

public sealed class QdrantOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
    public int VectorSize { get; set; } = 768;
    public string CollectionName { get; set; } = "dnd_chunks";
}
