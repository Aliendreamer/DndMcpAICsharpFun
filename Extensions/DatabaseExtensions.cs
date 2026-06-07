using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Campaign;
using DndMcpAICsharpFun.Features.Chat;

namespace DndMcpAICsharpFun.Extensions;

internal static class DatabaseExtensions
{
    internal static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration config)
    {
        var companionDb = config["Data:CompanionDb"] ?? "data/companion.db";
        var dbDir = Path.GetDirectoryName(companionDb);
        if (!string.IsNullOrEmpty(dbDir)) Directory.CreateDirectory(dbDir);
        var connectionString = $"Data Source={companionDb}";

        services.AddSingleton(new UserRepository(connectionString));
        services.AddSingleton(new CampaignRepository(connectionString));
        services.AddSingleton(new HeroRepository(connectionString));
        services.AddSingleton(new ChatRateLimiter(config.GetValue("RateLimit:MessagesPerMinute", 10)));
        return services;
    }
}
