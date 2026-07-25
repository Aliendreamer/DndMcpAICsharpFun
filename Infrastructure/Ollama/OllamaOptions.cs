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

    /// <summary>
    /// Per-call timeout (seconds) for a single extraction/grounding chat generation. The extraction
    /// OllamaApiClient uses an infinite HttpClient timeout (model cold-start can take minutes), so a
    /// hung generation would otherwise block the whole run forever with no checkpoint progress — a
    /// giant stat block that qwen3:8b never returns from wedged a real run. This bounds each call: on
    /// timeout the candidate is recorded as a failed extraction and the run continues to the next one.
    /// Generous by default so legitimate long generations are not cut off.
    /// </summary>
    [Range(30, 3600)]
    public int ChatTimeoutSeconds { get; set; } = 240;
}