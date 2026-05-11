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
    public void SelectTopN_ReturnsCandidatesInDescendingScoreOrder()
    {
        var reranker = DisabledReranker();
        var candidates = new[]
        {
            MakeResult("low"),
            MakeResult("high"),
            MakeResult("mid")
        };
        var scores = new float[] { 0.1f, 0.9f, 0.5f };

        var results = reranker.SelectTopN(candidates, scores, topN: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("high", results[0].Text);
        Assert.Equal("mid", results[1].Text);
    }

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
