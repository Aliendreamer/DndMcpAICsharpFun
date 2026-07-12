using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Infrastructure.Qdrant;

using FluentAssertions;

using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class RagRetrievalServiceSourceBooksFilterTests
{
    private readonly IQdrantSearchClient _qdrant = Substitute.For<IQdrantSearchClient>();
    private readonly IEmbeddingService _embedding = Substitute.For<IEmbeddingService>();

    private RagRetrievalService BuildSut()
    {
        var opts = new RerankerOptions { Enabled = false };
        var reranker = Substitute.For<IReranker>();
        reranker.Enabled.Returns(false);
        var rerankSvc = new RerankingService(reranker, Options.Create(opts));
        return new(_qdrant, _embedding,
            Options.Create(new QdrantOptions { BlocksCollectionName = "test-col" }),
            Options.Create(new RetrievalOptions { MaxTopK = 20, ScoreThreshold = 0.5f }),
            new QdrantSparseState { SparseSupported = false },
            rerankSvc,
            Options.Create(opts));
    }

    private void SetupEmbed() =>
        _embedding.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<float[]>>([new float[] { 0.1f, 0.2f }]));

    [Fact]
    public async Task SourceBooks_set_builds_an_or_condition_over_the_books()
    {
        SetupEmbed();
        Filter? captured = null;
        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Do<Filter?>(f => captured = f), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>([]));
        var sut = BuildSut();

        await sut.SearchAsync(new RetrievalQuery("who rules Sharn", SourceBooks: new[] { "ERLW", "PHB" }));

        captured.Should().NotBeNull();
        var orCondition = captured!.Must.Single(c => c.Filter is not null);
        var shouldKeywords = orCondition.Filter.Should
            .Select(s => s.Field.Match.Keyword).ToList();
        shouldKeywords.Should().BeEquivalentTo(new[] { "ERLW", "PHB" });
    }

    [Fact]
    public async Task Empty_source_books_adds_no_source_book_restriction()
    {
        SetupEmbed();
        Filter? captured = null;
        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Do<Filter?>(f => captured = f), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>([]));
        var sut = BuildSut();

        await sut.SearchAsync(new RetrievalQuery("q", SourceBooks: Array.Empty<string>()));

        (captured?.Must.Any(c => c.Filter is not null) ?? false).Should().BeFalse();
    }
}
