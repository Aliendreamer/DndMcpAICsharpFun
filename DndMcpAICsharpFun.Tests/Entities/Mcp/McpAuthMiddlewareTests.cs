using DndMcpAICsharpFun.Features.Mcp;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Mcp;

public class McpAuthMiddlewareTests
{
    [Fact]
    public async Task Missing_key_returns_401()
    {
        var next = new RequestDelegate(_ => Task.CompletedTask);
        var mw = new McpAuthMiddleware(next, Options.Create(new McpOptions { ApiKey = "secret" }));
        var ctx = new DefaultHttpContext();

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Wrong_key_returns_401()
    {
        var nextCalled = false;
        var next = new RequestDelegate(_ => { nextCalled = true; return Task.CompletedTask; });
        var mw = new McpAuthMiddleware(next, Options.Create(new McpOptions { ApiKey = "secret" }));
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Mcp-Api-Key"] = "wrong";

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Correct_key_calls_next()
    {
        var nextCalled = false;
        var next = new RequestDelegate(_ => { nextCalled = true; return Task.CompletedTask; });
        var mw = new McpAuthMiddleware(next, Options.Create(new McpOptions { ApiKey = "secret" }));
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Mcp-Api-Key"] = "secret";

        await mw.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_configured_key_returns_401()
    {
        var next = new RequestDelegate(_ => Task.CompletedTask);
        var mw = new McpAuthMiddleware(next, Options.Create(new McpOptions { ApiKey = "" }));
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Mcp-Api-Key"] = "anything";

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
    }
}