using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Chat;
using DndMcpAICsharpFun.Infrastructure.Ollama;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Admin;

public sealed class EvalAdminEndpointsTests
{
    private const string ValidKey = "test-admin-key";

    private sealed class FakeRunner(Func<long, ChatTurnResult> onRun) : IEvalTurnRunner
    {
        public long? SeenUserId;
        public Task<ChatTurnResult> RunAsync(string prompt, long userId, bool allowWebSearch, CancellationToken ct)
        {
            SeenUserId = userId;
            return Task.FromResult(onRun(userId));
        }
    }

    private sealed class FakeUsers(long? id) : IEvalUserResolver
    {
        public Task<long?> ResolveTestUserAsync(CancellationToken ct) => Task.FromResult(id);
    }

    private static ChatTurnResult Sample() => new("hello", ["lookup"],
        [new ToolCallTrace("lookup", "{}", "r", null)], 12);

    private static async Task<HttpClient> BuildAsync(IEvalTurnRunner runner, long? testUser, bool key = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(runner);
        builder.Services.AddSingleton<IEvalUserResolver>(new FakeUsers(testUser));
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = ValidKey);
        builder.Services.Configure<OllamaOptions>(o => o.ChatModel = "qwen-test");
        var app = builder.Build();
        app.UseWhen(
            static ctx => ctx.Request.Path.StartsWithSegments("/admin"),
            static branch => branch.UseMiddleware<AdminApiKeyMiddleware>());
        app.MapGroup("/admin").MapEvalAdmin();
        await app.StartAsync();
        var client = app.GetTestClient();
        if (key) client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ValidKey);
        return client;
    }

    [Fact]
    public async Task NoAdminKey_Returns401()
    {
        var client = await BuildAsync(new FakeRunner(_ => Sample()), 42, key: false);
        var r = await client.PostAsJsonAsync("/admin/eval/turn", new { prompt = "hi" });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidRequest_ReturnsTraceJson()
    {
        var client = await BuildAsync(new FakeRunner(_ => Sample()), 42);
        var r = await client.PostAsJsonAsync("/admin/eval/turn", new { prompt = "hi" });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("model").GetString().Should().Be("qwen-test");
        doc.RootElement.GetProperty("text").GetString().Should().Be("hello");
        doc.RootElement.GetProperty("offeredTools")[0].GetString().Should().Be("lookup");
        doc.RootElement.GetProperty("latencyMs").GetInt64().Should().Be(12);
        doc.RootElement.GetProperty("toolCalls")[0].GetProperty("name").GetString().Should().Be("lookup");
    }

    [Fact]
    public async Task NoUserId_ResolvesTestUser()
    {
        var runner = new FakeRunner(_ => Sample());
        var client = await BuildAsync(runner, 42);
        await client.PostAsJsonAsync("/admin/eval/turn", new { prompt = "hi" });
        runner.SeenUserId.Should().Be(42);
    }

    [Fact]
    public async Task ExplicitUserId_IsUsed()
    {
        var runner = new FakeRunner(_ => Sample());
        var client = await BuildAsync(runner, 42);
        await client.PostAsJsonAsync("/admin/eval/turn", new { prompt = "hi", userId = 7 });
        runner.SeenUserId.Should().Be(7);
    }

    [Fact]
    public async Task NoUserId_TestUserMissing_Returns400()
    {
        var client = await BuildAsync(new FakeRunner(_ => Sample()), null);
        var r = await client.PostAsJsonAsync("/admin/eval/turn", new { prompt = "hi" });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EmptyPrompt_Returns400()
    {
        var client = await BuildAsync(new FakeRunner(_ => Sample()), 42);
        var r = await client.PostAsJsonAsync("/admin/eval/turn", new { prompt = "  " });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RunnerThrows_Returns502()
    {
        var client = await BuildAsync(new FakeRunner(_ => throw new InvalidOperationException("boom")), 42);
        var r = await client.PostAsJsonAsync("/admin/eval/turn", new { prompt = "hi" });
        r.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task RunnerTimesOut_Returns502()
    {
        // An HttpClient timeout surfaces as TaskCanceledException (an OperationCanceledException)
        // while the request token is NOT cancelled — must be a 502, not an unhandled 500.
        var client = await BuildAsync(new FakeRunner(_ => throw new TaskCanceledException("HttpClient.Timeout of 100 seconds elapsing")), 42);
        var r = await client.PostAsJsonAsync("/admin/eval/turn", new { prompt = "hi" });
        r.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }
}