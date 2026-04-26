using DndMcpAICsharpFun.Features.Admin;
using Microsoft.AspNetCore.Http;

namespace DndMcpAICsharpFun.Tests.Admin;

public sealed class AdminApiKeyMiddlewareTests
{
    private const string ValidKey = "super-secret";

    private static (AdminApiKeyMiddleware Middleware, bool NextCalled) Build(string? headerValue)
    {
        bool nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var opts = Options.Create(new AdminOptions { ApiKey = ValidKey });
        var middleware = new AdminApiKeyMiddleware(next, opts);
        return (middleware, nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_ValidKey_CallsNext()
    {
        bool nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var opts = Options.Create(new AdminOptions { ApiKey = ValidKey });
        var middleware = new AdminApiKeyMiddleware(next, opts);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Admin-Api-Key"] = ValidKey;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.NotEqual(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_MissingHeader_Returns401()
    {
        bool nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var opts = Options.Create(new AdminOptions { ApiKey = ValidKey });
        var middleware = new AdminApiKeyMiddleware(next, opts);

        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_WrongKey_Returns401()
    {
        bool nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var opts = Options.Create(new AdminOptions { ApiKey = ValidKey });
        var middleware = new AdminApiKeyMiddleware(next, opts);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Admin-Api-Key"] = "wrong-key";

        await middleware.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
        Assert.False(nextCalled);
    }
}
