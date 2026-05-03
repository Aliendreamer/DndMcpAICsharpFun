namespace DndMcpAICsharpFun.Infrastructure.Docling;

public sealed class DoclingOptions
{
    public string BaseUrl { get; set; } = "http://docling:5001";
    public int RequestTimeoutSeconds { get; set; } = 1800;
}
