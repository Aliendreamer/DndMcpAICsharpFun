using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Mcp;

public sealed class McpAuthMiddleware(RequestDelegate next, IOptions<McpOptions> options)
{
    private const string HeaderName = "X-Mcp-Api-Key";
    private readonly string _apiKey = options.Value.ApiKey;

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (string.IsNullOrEmpty(_apiKey) ||
            !ctx.Request.Headers.TryGetValue(HeaderName, out var key) ||
            key.ToString() != _apiKey)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await next(ctx);
    }
}
