using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace DndMcpAICsharpFun.Extensions;

internal static class RateLimitExtensions
{
    internal static IServiceCollection AddDndRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        services.AddMemoryCache();
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
            options.AddFixedWindowLimiter("registration", o =>
            {
                o.PermitLimit = 5;
                o.Window = TimeSpan.FromMinutes(10);
                o.QueueLimit = 0;
                o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });
        return services;
    }
}
