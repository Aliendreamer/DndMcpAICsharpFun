using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Features.Downtime;

internal static class DowntimeServiceCollectionExtensions
{
    internal static IServiceCollection AddDowntime(this IServiceCollection services)
    {
        services.AddScoped<DowntimeService>();
        return services;
    }
}
