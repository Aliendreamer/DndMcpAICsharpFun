using DndMcpAICsharpFun.Features.Chat;

using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Tests.Chat;

public sealed class McpToolsProviderTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static IOptions<McpClientOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(
            new McpClientOptions { Url = "http://localhost/mcp", ApiKey = "test-key" });

    [Fact]
    public async Task FirstCallFails_ReturnsEmptyTools_DoesNotThrow()
    {
        var provider = new McpToolsProvider(
            Options(),
            NullLogger<McpToolsProvider>.Instance,
            connect: _ => Task.FromException<(IAsyncDisposable?, IReadOnlyList<AITool>)>(
                new InvalidOperationException("connection refused")));

        var tools = await provider.GetToolsAsync();

        Assert.Empty(tools);
    }

    [Fact]
    public async Task SecondCall_WithinCooldown_DoesNotRetryConnection()
    {
        var clock = new ManualTimeProvider();
        var callCount = 0;
        var provider = new McpToolsProvider(
            Options(),
            NullLogger<McpToolsProvider>.Instance,
            clock,
            connect: _ =>
            {
                callCount++;
                return Task.FromException<(IAsyncDisposable?, IReadOnlyList<AITool>)>(
                    new InvalidOperationException("connection refused"));
            });

        var first = await provider.GetToolsAsync();
        clock.Advance(McpToolsProvider.RetryCooldown - TimeSpan.FromSeconds(1));
        var second = await provider.GetToolsAsync();

        Assert.Empty(first);
        Assert.Empty(second);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task AfterCooldown_RetriesConnection_AndSucceeds()
    {
        var clock = new ManualTimeProvider();
        var callCount = 0;
        var expectedTools = new List<AITool> { AIFunctionFactory.Create(() => "ok", name: "dummy_tool") };
        var provider = new McpToolsProvider(
            Options(),
            NullLogger<McpToolsProvider>.Instance,
            clock,
            connect: _ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return Task.FromException<(IAsyncDisposable?, IReadOnlyList<AITool>)>(
                        new InvalidOperationException("connection refused"));
                }
                return Task.FromResult<(IAsyncDisposable?, IReadOnlyList<AITool>)>((null, expectedTools));
            });

        var first = await provider.GetToolsAsync();
        clock.Advance(McpToolsProvider.RetryCooldown + TimeSpan.FromSeconds(1));
        var second = await provider.GetToolsAsync();

        Assert.Empty(first);
        Assert.Same(expectedTools, second);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task AfterSuccess_SubsequentCalls_ReturnCachedTools_WithoutReconnecting()
    {
        var callCount = 0;
        var expectedTools = new List<AITool> { AIFunctionFactory.Create(() => "ok", name: "dummy_tool") };
        var provider = new McpToolsProvider(
            Options(),
            NullLogger<McpToolsProvider>.Instance,
            connect: _ =>
            {
                callCount++;
                return Task.FromResult<(IAsyncDisposable?, IReadOnlyList<AITool>)>((null, expectedTools));
            });

        var first = await provider.GetToolsAsync();
        var second = await provider.GetToolsAsync();

        Assert.Same(expectedTools, first);
        Assert.Same(first, second);
        Assert.Equal(1, callCount);
    }
}