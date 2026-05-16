using Microsoft.Extensions.Options;

using System.Security.Cryptography;

namespace DndMcpAICsharpFun.Features.Mcp;

public sealed class McpAuthMiddleware(RequestDelegate next, IOptions<McpOptions> options)
{
    private const string HeaderName = "X-Mcp-Api-Key";
    private readonly string _apiKey = options.Value.ApiKey;

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (string.IsNullOrEmpty(_apiKey) ||
            !ctx.Request.Headers.TryGetValue(HeaderName, out var key) ||
            !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(key.ToString()),
                System.Text.Encoding.UTF8.GetBytes(_apiKey)))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await next(ctx);
    }
}
