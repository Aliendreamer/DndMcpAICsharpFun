namespace DndMcpAICsharpFun.Features.SessionPrep;

internal static class SessionPrepServiceCollectionExtensions
{
    internal static IServiceCollection AddSessionPrep(this IServiceCollection services)
    {
        services.AddScoped<SessionPrepService>();
        return services;
    }
}
