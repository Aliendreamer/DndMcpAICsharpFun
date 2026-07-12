using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Features.Lore;

internal static class LoreServiceCollectionExtensions
{
    internal static IServiceCollection AddLore(this IServiceCollection services)
    {
        services.AddScoped<SettingLoreService>();
        return services;
    }
}
