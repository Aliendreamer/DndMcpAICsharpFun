using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DndMcpAICompanion.Extensions;

internal static class RateLimitExtensions
{
    internal static IServiceCollection AddDndRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var requestsPerMinute = config.GetValue("RateLimit:RequestsPerMinute", 60);
        services.AddRateLimiter(options =>
        {
            options.AddSlidingWindowLimiter("global", o =>
            {
                o.PermitLimit = requestsPerMinute;
                o.Window = TimeSpan.FromMinutes(1);
                o.SegmentsPerWindow = 6;
                o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                o.QueueLimit = 0;
            });
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });
        return services;
    }
}
