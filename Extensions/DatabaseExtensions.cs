using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Chat;

namespace DndMcpAICsharpFun.Extensions;

internal static class DatabaseExtensions
{
    internal static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration config)
    {
        // AppDbContext + its factory are registered in AddInfrastructureClients; repositories
        // resolve the factory and create short-lived contexts per operation (Blazor-safe).
        services.AddSingleton<UserRepository>();
        services.AddSingleton<CampaignRepository>();
        services.AddSingleton<HeroRepository>();
        services.AddSingleton<ChatRepository>();
        services.AddSingleton(new ChatRateLimiter(config.GetValue("RateLimit:MessagesPerMinute", 10)));
        return services;
    }
}
