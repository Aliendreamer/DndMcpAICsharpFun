using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Features.CharacterAdvice;

internal static class CharacterAdviceServiceCollectionExtensions
{
    internal static IServiceCollection AddCharacterAdvice(this IServiceCollection services)
    {
        services.AddScoped<LevelUpPlanner>();
        services.AddScoped<EntityOptionProvider>();
        services.AddScoped<LevelUpAdviceService>();
        return services;
    }
}
