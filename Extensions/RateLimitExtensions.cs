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
            // Per-client partitioned window: each authenticated user (or client IP when anonymous)
            // gets its own bucket, so one caller cannot exhaust the limit for everyone.
            options.AddPolicy("global", context => RateLimitPartition.GetSlidingWindowLimiter(
                PartitionKey(context),
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = requestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }));

            // Public retrieval surface: tighter per-client budget for anonymous, unauthenticated calls.
            var retrievalPerMinute = config.GetValue("RateLimit:RetrievalRequestsPerMinute", 30);
            options.AddPolicy("retrieval", context => RateLimitPartition.GetSlidingWindowLimiter(
                PartitionKey(context),
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = retrievalPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }));

            // Admin + MCP surface: key-gated already (AdminApiKeyMiddleware / McpAuthMiddleware), so
            // this is a generous secondary throttle against a leaked key driving runaway automated
            // calls to expensive endpoints (extract-entities, ingest-blocks) — not a primary control.
            var adminMcpPerMinute = config.GetValue("RateLimit:AdminMcpRequestsPerMinute", 120);
            options.AddPolicy("adminMcp", context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = adminMcpPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }));

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });
        return services;
    }

    // Partition key: authenticated user id when present, else the (forwarded-header-aware) client IP.
    private static string PartitionKey(HttpContext context)
    {
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
            return $"user:{userId}";
        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}