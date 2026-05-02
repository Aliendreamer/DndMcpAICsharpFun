namespace DndMcpAICsharpFun.Infrastructure.Ollama;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public string ExtractionModel { get; set; } = "llama3.2";
    public int ExtractionNumCtx { get; set; } = 8192;
    public int ExtractionTimeoutSeconds { get; set; } = 120;
}
