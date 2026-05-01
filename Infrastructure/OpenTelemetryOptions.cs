using System.Diagnostics.CodeAnalysis;

namespace DndMcpAICsharpFun.Infrastructure;

[ExcludeFromCodeCoverage]
internal sealed class OpenTelemetryOptions
{
    public bool Enabled { get; init; }
    public string ServiceName { get; init; } = "dnd-mcp-api";
}
