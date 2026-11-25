using DndMcpAICsharpFun.Features.Retrieval;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class RerankByPieceTests
{
    /// <summary>Scores a passage by whether it holds the needle; records what it was asked.</summary>
    private sealed class NeedleReranker(string needle, bool enabled = true) : IReranker
    {
        public List<string> Seen { get; } = [];
        public bool Enabled => enabled;

        public Task<float[]> RerankAsync(string query, IReadOnlyList<string> passages, CancellationToken ct)
        {
            Seen.AddRange(passages);
            return Task.FromResult(passages.Select(p => p.Contains(needle) ? 1f : 0f).ToArray());
        }
    }

    private static string Long(string ending) => string.Concat(Enumerable.Repeat("Plain filler text here. ", 400)) + ending;

    private static RerankingService Service(IReranker r) => new(r, Options.Create(new RerankerOptions { Enabled = true }));

    [Fact]
    public async Task A_match_at_the_end_of_a_long_text_wins()
    {
        var deep = Long("The two-headed prince.");
        var result = await Service(new NeedleReranker("two-headed")).RerankByPieceAsync(
            "demon with two-headed body", new[] { "short text", deep }, s => s, 2, CancellationToken.None);
        result[0].Candidate.Should().Be(deep);
        result[0].Best.Text.Should().Contain("two-headed");
    }

    [Fact]
    public async Task At_most_four_pieces_per_candidate_are_scored_and_the_opening_is_one_of_them()
    {
        var reranker = new NeedleReranker("nothing");
        await Service(reranker).RerankByPieceAsync("ending words", new[] { "OPENING " + Long("ending words") }, s => s, 1, CancellationToken.None);
        reranker.Seen.Should().HaveCountLessThanOrEqualTo(4).And.Contain(p => p.StartsWith("OPENING"));
    }

    [Fact]
    public async Task Without_a_reranker_the_order_stays_and_the_best_piece_holds_the_query_words()
    {
        var result = await Service(new NeedleReranker("x", enabled: false)).RerankByPieceAsync(
            "strange ending", new[] { Long("A strange ending."), "second" }, s => s, 2, CancellationToken.None);
        result.Select(r => r.Candidate).Should().HaveElementAt(1, "second");
        result[0].Best.Text.Should().Contain("strange ending");
    }
}