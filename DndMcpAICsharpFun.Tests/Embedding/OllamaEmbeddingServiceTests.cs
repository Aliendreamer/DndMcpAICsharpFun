using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;

namespace DndMcpAICsharpFun.Tests.Embedding;

public sealed class OllamaEmbeddingServiceTests
{
    private static OllamaEmbeddingService BuildSut(IOllamaApiClient client, string embeddingModel = "nomic-embed-text")
        => new(
            client,
            Options.Create(new OllamaOptions { EmbeddingModel = embeddingModel }),
            NullLogger<OllamaEmbeddingService>.Instance);

    [Fact]
    public async Task EmbedAsync_Success_ReturnsEmbeddingsFromResponse()
    {
        var client = Substitute.For<IOllamaApiClient>();
        client.EmbedAsync(Arg.Any<EmbedRequest>(), Arg.Any<CancellationToken>())
            .Returns(new EmbedResponse { Embeddings = [[1f, 2f, 3f]] });

        var sut = BuildSut(client);
        var result = await sut.EmbedAsync(["hello"]);

        Assert.Single(result);
        Assert.Equal([1f, 2f, 3f], result[0]);
        await client.Received(1).EmbedAsync(
            Arg.Is<EmbedRequest>(r => r.Model == "nomic-embed-text" && r.Input != null && r.Input.SequenceEqual(new[] { "hello" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmbedAsync_ForwardsCancellationToken()
    {
        var client = Substitute.For<IOllamaApiClient>();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        client.EmbedAsync(Arg.Any<EmbedRequest>(), Arg.Is<CancellationToken>(ct => ct.IsCancellationRequested))
            .Returns(new EmbedResponse { Embeddings = [[0f]] });

        var sut = BuildSut(client);
        await sut.EmbedAsync(["hello"], cts.Token);

        await client.Received(1).EmbedAsync(
            Arg.Any<EmbedRequest>(),
            Arg.Is<CancellationToken>(ct => ct.IsCancellationRequested));
    }

    [Fact]
    public async Task EmbedAsync_HttpRequestException_WrapsAsInvalidOperationException()
    {
        var client = Substitute.For<IOllamaApiClient>();
        var original = new HttpRequestException("connection refused");
        client.EmbedAsync(Arg.Any<EmbedRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<EmbedResponse>>(_ => throw original);

        var sut = BuildSut(client);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.EmbedAsync(["hello"]));

        Assert.Contains("nomic-embed-text", ex.Message);
        Assert.Same(original, ex.InnerException);
    }
}
