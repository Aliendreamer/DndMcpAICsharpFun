using DndMcpAICsharpFun.Features.Retrieval;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class RerankingServiceTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static RerankingService BuildSut(
        IReranker reranker,
        bool globalEnabled = true,
        int candidatePoolSize = 20) =>
        new(reranker, Options.Create(new RerankerOptions
        {
            Enabled = globalEnabled,
            CandidatePoolSize = candidatePoolSize,
            RerankBlocks = true,
            RerankEntities = true,
        }));

    private static string Identity(string s) => s;

    // ── Task 1.1 / Spec: "Service reranks and truncates" ─────────────────────

    [Fact]
    public async Task RerankAsync_ReranksAndTruncatesToFinalTopN()
    {
        // Arrange: reranker returns scores that invert original order
        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);
        // candidates: ["a","b","c","d","e"] with scores [0.1,0.5,0.9,0.4,0.2]
        // => ranked order: c(0.9), b(0.5), d(0.4), e(0.2), a(0.1)
        var candidates = new[] { "a", "b", "c", "d", "e" };
        mockReranker
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.1f, 0.5f, 0.9f, 0.4f, 0.2f }));

        var sut = BuildSut(mockReranker);

        // Act
        var result = await sut.RerankAsync("query", candidates, Identity, finalTopN: 3, CancellationToken.None);

        // Assert: top 3 by score
        result.Should().HaveCount(3);
        result[0].Should().Be("c");
        result[1].Should().Be("b");
        result[2].Should().Be("d");
    }

    [Fact]
    public async Task RerankAsync_RespectsTopN_LargerThanCandidates()
    {
        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);
        var candidates = new[] { "x", "y" };
        mockReranker
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.9f, 0.1f }));

        var sut = BuildSut(mockReranker);

        var result = await sut.RerankAsync("query", candidates, Identity, finalTopN: 10, CancellationToken.None);

        result.Should().HaveCount(2); // fewer candidates than topN
        result[0].Should().Be("x");
    }

    // ── Task 1.1 / Spec: "Disabled reranking is a stable passthrough" ────────

    [Fact]
    public async Task RerankAsync_WhenGloballyDisabled_ReturnsFirstTopNInOriginalOrder()
    {
        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true); // model is ready, but global flag is off
        var candidates = new[] { "alpha", "beta", "gamma", "delta" };

        var sut = BuildSut(mockReranker, globalEnabled: false);

        var result = await sut.RerankAsync("query", candidates, Identity, finalTopN: 2, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Should().Be("alpha");
        result[1].Should().Be("beta");

        // Model must NOT be called
        await mockReranker.DidNotReceive()
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RerankAsync_WhenRerankerNotEnabled_ReturnsFirstTopNWithoutModelCall()
    {
        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(false); // model unavailable
        var candidates = new[] { "one", "two", "three" };

        var sut = BuildSut(mockReranker, globalEnabled: true);

        var result = await sut.RerankAsync("query", candidates, Identity, finalTopN: 2, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Should().Be("one");
        result[1].Should().Be("two");

        await mockReranker.DidNotReceive()
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RerankAsync_EmptyCandidates_ReturnsEmpty()
    {
        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);

        var sut = BuildSut(mockReranker);

        var result = await sut.RerankAsync("query", Array.Empty<string>(), Identity, finalTopN: 5, CancellationToken.None);

        result.Should().BeEmpty();
        await mockReranker.DidNotReceive()
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }
}