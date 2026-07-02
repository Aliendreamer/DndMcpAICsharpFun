using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class CrossEncoderRerankerTests
{
    private static CrossEncoderReranker DisabledReranker() =>
        new(new RerankerOptions { Enabled = false }, NullLogger<CrossEncoderReranker>.Instance);

    private static RetrievalResult MakeResult(string text) =>
        new(text, new ChunkMetadata("PHB", DndVersion.Edition2014, ContentCategory.Spell, null, "Ch1", 1, 0), 0.5f);

    [Fact]
    public async Task RerankAsync_WhenDisabled_ReturnsZeroScores()
    {
        var reranker = DisabledReranker();
        var passages = new[] { "passage one", "passage two" };

        var scores = await reranker.RerankAsync("query", passages, CancellationToken.None);

        Assert.Equal(2, scores.Length);
        Assert.All(scores, s => Assert.Equal(0f, s));
    }
}
