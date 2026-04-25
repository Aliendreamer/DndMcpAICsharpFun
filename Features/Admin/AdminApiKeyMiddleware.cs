using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin;

public sealed class AdminApiKeyMiddleware(RequestDelegate next, IOptions<AdminOptions> options)
{
    private const string HeaderName = "X-Admin-Api-Key";
    private readonly string _apiKey = options.Value.ApiKey;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided) ||
            provided != _apiKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
