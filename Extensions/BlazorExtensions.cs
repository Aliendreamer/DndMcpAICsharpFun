namespace DndMcpAICsharpFun.Extensions;

internal static class BlazorExtensions
{
    internal static IServiceCollection AddDndBlazor(this IServiceCollection services)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();
        return services;
    }
}
