using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Rules;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Rules;

public sealed class RulesAdjudicationServiceTests
{
    private static List<RetrievalResult> Results(params (string text, string book)[] rows) =>
        rows.Select(r => new RetrievalResult(
            r.text,
            new ChunkMetadata(
                SourceBook: r.book,
                Version: DndVersion.Edition2014,
                Category: ContentCategory.Rule,
                EntityName: null,
                Chapter: "Combat",
                PageNumber: 0,
                ChunkIndex: 0,
                SectionTitle: "Grappling"),
            Score: 0.9f)).ToList();

    [Fact]
    public async Task Scopes_retrieval_to_the_core_rulebooks_at_higher_topK()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Results(("Grappling: ...", "PlayerHandbook 2014")));
        var svc = new RulesAdjudicationService(rag);

        await svc.AskAsync("grapple while prone", edition: null, CancellationToken.None);

        await rag.Received().SearchAsync(
            Arg.Is<RetrievalQuery>(q =>
                q.SourceBooks != null
                && q.SourceBooks.Contains("PlayerHandbook 2014")
                && q.SourceBooks.Contains("Dungeon Master's Guide 2014")
                && q.TopK == RuleSources.TopK),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Projects_results_to_cited_passages()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Results(("Grappling rule text", "PlayerHandbook 2014")));
        var svc = new RulesAdjudicationService(rag);

        var result = await svc.AskAsync("grappling", edition: null, CancellationToken.None);

        result.Passages.Should().ContainSingle();
        result.Passages[0].SourceBook.Should().Be("PlayerHandbook 2014");
    }

    [Fact]
    public async Task Empty_retrieval_returns_explicit_empty()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<RetrievalResult>());
        var svc = new RulesAdjudicationService(rag);

        (await svc.AskAsync("nonsense", edition: null, CancellationToken.None)).Passages.Should().BeEmpty();
    }
}
