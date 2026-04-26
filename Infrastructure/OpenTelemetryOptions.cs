namespace DndMcpAICsharpFun.Infrastructure;

internal sealed class OpenTelemetryOptions
{
    public bool Enabled { get; init; }
    public string ServiceName { get; init; } = "dnd-mcp-api";
}
