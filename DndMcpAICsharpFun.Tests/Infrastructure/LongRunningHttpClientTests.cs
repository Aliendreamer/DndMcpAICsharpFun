using DndMcpAICsharpFun.Features.Chat;
using DndMcpAICsharpFun.Infrastructure.Http;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Infrastructure;

public sealed class LongRunningHttpClientTests
{
    [Fact]
    public void Create_UsesTheGivenTimeout_NotTheDefault100Seconds()
    {
        using var client = LongRunningHttpClient.Create(new Uri("http://ollama:11434"), 600);

        client.Timeout.Should().Be(TimeSpan.FromSeconds(600));
        client.BaseAddress.Should().Be(new Uri("http://ollama:11434"));
    }

    [Fact]
    public void Create_WithoutBaseAddress_LeavesItUnset()
    {
        using var client = LongRunningHttpClient.Create(null, 300);

        client.Timeout.Should().Be(TimeSpan.FromSeconds(300));
        client.BaseAddress.Should().BeNull();
    }

    [Fact]
    public void ChatAndMcpTimeoutDefaults_AreWellAbove100Seconds()
    {
        new OllamaOptions().ChatTimeoutSeconds.Should().BeGreaterThan(100);
        new McpClientOptions().TimeoutSeconds.Should().BeGreaterThan(100);
    }
}
