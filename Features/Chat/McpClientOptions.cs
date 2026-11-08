namespace DndMcpAICsharpFun.Features.Chat;

public sealed class McpClientOptions
{
    public string Url { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Timeout for the chat's loopback MCP calls; tools such as the coach drive the model themselves.</summary>
    public int TimeoutSeconds { get; init; } = 600;
}