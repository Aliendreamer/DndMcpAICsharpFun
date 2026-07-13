using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Features.Npc;

internal static class NpcServiceCollectionExtensions
{
    internal static IServiceCollection AddNpc(this IServiceCollection services)
    {
        services.AddScoped<NpcGenerationService>();
        return services;
    }
}
