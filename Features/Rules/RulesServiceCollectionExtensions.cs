using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Features.Rules;

internal static class RulesServiceCollectionExtensions
{
    internal static IServiceCollection AddRules(this IServiceCollection services)
    {
        services.AddScoped<RulesAdjudicationService>();
        return services;
    }
}
