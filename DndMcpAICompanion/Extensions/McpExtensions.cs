using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace DndMcpAICompanion.Extensions;

internal static class McpExtensions
{
    internal static IServiceCollection AddMcpClient(
        this IServiceCollection services,
        McpClient mcpClient,
        IReadOnlyList<AITool> mcpTools)
    {
        services.AddSingleton(mcpClient);
        services.AddSingleton<IReadOnlyList<AITool>>(mcpTools);
        return services;
    }
}
