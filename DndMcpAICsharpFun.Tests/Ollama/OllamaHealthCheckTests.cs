using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OllamaSharp;
using OllamaSharp.Models;

namespace DndMcpAICsharpFun.Tests.Ollama;

public sealed class OllamaHealthCheckTests
{
    private static OllamaHealthCheck BuildSut(IOllamaApiClient client)
        => new(client);

    [Fact]
    public async Task CheckHealthAsync_WhenListLocalModelsSucceeds_ReturnsHealthy()
    {
        var client = Substitute.For<IOllamaApiClient>();
        client.ListLocalModelsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Model>>(Array.Empty<Model>()));

        var sut = BuildSut(client);
        var result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenListLocalModelsFails_ReturnsUnhealthy()
    {
        var client = Substitute.For<IOllamaApiClient>();
        var thrownException = new Exception("unreachable");
        client.ListLocalModelsAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IEnumerable<Model>>>(_ => throw thrownException);

        var sut = BuildSut(client);
        var result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Ollama is unreachable", result.Description);
        Assert.Same(thrownException, result.Exception);
    }
}
