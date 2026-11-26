using System.Net;
using System.Text;
using System.Text.Json;

using DndMcpAICsharpFun.Extensions;

using FluentAssertions;

using Microsoft.Extensions.AI;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Chat;

public sealed class ChatContextSizeTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"model":"m","created_at":"2026-01-01T00:00:00Z","message":{"role":"assistant","content":"hi"},"done":true}""" + "\n",
                    Encoding.UTF8, "application/x-ndjson"),
            };
        }
    }

    [Fact]
    public async Task Every_chat_request_carries_num_ctx_and_leaves_reasoning_alone()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://ollama.test") };
        var client = ChatExtensions.BuildChatClient(http, "m", 8192);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], new ChatOptions());

        using var doc = JsonDocument.Parse(handler.Body);
        doc.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32().Should().Be(8192);
        if (doc.RootElement.TryGetProperty("think", out var think)) think.ValueKind.Should().Be(JsonValueKind.Null);
    }


    [Fact]
    public async Task Every_chat_request_carries_qwen3_thinking_sampling_a_seed_and_a_length_cap()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://ollama.test") };
        var client = ChatExtensions.BuildChatClient(http, "m", 8192);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], new ChatOptions());

        using var doc = JsonDocument.Parse(handler.Body);
        var options = doc.RootElement.GetProperty("options");
        options.GetProperty("temperature").GetSingle().Should().Be(0.6f);
        options.GetProperty("top_p").GetSingle().Should().Be(0.95f);
        options.GetProperty("top_k").GetInt32().Should().Be(20);
        options.GetProperty("min_p").GetSingle().Should().Be(0);
        options.GetProperty("seed").GetInt32().Should().Be(42);
        options.GetProperty("num_predict").GetInt32().Should().Be(4096);
    }


    private sealed class AlwaysCallsAToolHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"model":"m","created_at":"2026-01-01T00:00:00Z","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"missing_tool","arguments":{}}}]},"done":true}""" + "\n",
                    Encoding.UTF8, "application/x-ndjson"),
            });
        }
    }

    [Fact]
    public async Task A_model_that_keeps_calling_tools_is_stopped_after_a_few_rounds()
    {
        var handler = new AlwaysCallsAToolHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://ollama.test") };
        var client = ChatExtensions.BuildChatClient(http, "m", 8192);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { Tools = [AIFunctionFactory.Create(() => "ok", "real_tool")] });

        handler.Requests.Should().BeLessThanOrEqualTo(ChatExtensions.MaxToolRounds + 1);
    }
}