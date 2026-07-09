using System.ComponentModel.DataAnnotations;

namespace DndMcpAICsharpFun.Infrastructure.Ollama;

public sealed class OllamaOptions
{
    [Required]
    public string BaseUrl { get; set; } = "http://localhost:11434";
    [Required]
    public string EmbeddingModel { get; set; } = "mxbai-embed-large";
    [Required]
    public string ChatModel { get; set; } = "qwen3:8b";
}