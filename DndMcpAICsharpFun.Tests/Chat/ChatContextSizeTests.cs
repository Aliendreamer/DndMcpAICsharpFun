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
}