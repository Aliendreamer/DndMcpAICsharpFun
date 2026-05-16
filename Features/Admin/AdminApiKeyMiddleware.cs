using Microsoft.Extensions.Options;

using System.Security.Cryptography;

namespace DndMcpAICsharpFun.Features.Admin;

public sealed class AdminApiKeyMiddleware(RequestDelegate next, IOptions<AdminOptions> options)
{
    private const string HeaderName = "X-Admin-Api-Key";
    private readonly string _apiKey = options.Value.ApiKey;

    public async Task InvokeAsync(HttpContext context)
    {
        if (string.IsNullOrEmpty(_apiKey) ||
            !context.Request.Headers.TryGetValue(HeaderName, out var provided) ||
            !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(provided.ToString()),
                System.Text.Encoding.UTF8.GetBytes(_apiKey)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
