namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class AnthropicOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";
    public string ApiVersion { get; set; } = "2023-06-01";
    public string DefaultModel { get; set; } = "claude-sonnet-4-5-20250929";
    public string EscalationModel { get; set; } = "claude-opus-4-5-20250929";
    public int RequestTimeoutSeconds { get; set; } = 180;
    public int MaxOutputTokens { get; set; } = 4096;
}
