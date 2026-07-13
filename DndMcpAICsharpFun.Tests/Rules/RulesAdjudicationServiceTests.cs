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

        await svc.AskAsync("grapple while prone", ruleTopics: null, edition: null, CancellationToken.None);

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

        var result = await svc.AskAsync("grappling", ruleTopics: null, edition: null, CancellationToken.None);

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

        (await svc.AskAsync("nonsense", ruleTopics: null, edition: null, CancellationToken.None)).Passages.Should().BeEmpty();
    }

    [Fact]
    public async Task Multihop_runs_one_scoped_retrieval_per_topic_and_groups_them()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci => {
                var q = ci.Arg<RetrievalQuery>();
                // return one passage naming the topic's book, so we can see per-topic grouping
                return (IList<RetrievalResult>)Results((q.QueryText + " rule", "PlayerHandbook 2014"));
            });
        var svc = new RulesAdjudicationService(rag);

        var result = await svc.AskAsync("grapple while prone",
            ruleTopics: ["grappling", "prone condition"], edition: null, CancellationToken.None);

        // exactly one retrieval per topic, each scoped to the rulebooks at TopicTopK
        await rag.Received(1).SearchAsync(
            Arg.Is<RetrievalQuery>(q => q.QueryText == "grappling"
                && q.SourceBooks!.Contains("PlayerHandbook 2014") && q.TopK == RuleSources.TopicTopK),
            Arg.Any<CancellationToken>());
        await rag.Received(1).SearchAsync(
            Arg.Is<RetrievalQuery>(q => q.QueryText == "prone condition" && q.TopK == RuleSources.TopicTopK),
            Arg.Any<CancellationToken>());
        result.Topics.Select(t => t.Topic).Should().Equal("grappling", "prone condition");
        result.Topics.Should().OnlyContain(t => t.Passages.Count > 0);
    }

    [Fact]
    public async Task Multihop_dedupes_the_flat_passage_union()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        // same passage returned for BOTH topics
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Results(("shared rule text", "PlayerHandbook 2014")));
        var svc = new RulesAdjudicationService(rag);

        var result = await svc.AskAsync("q", ruleTopics: ["a", "b"], edition: null, CancellationToken.None);

        result.Passages.Should().ContainSingle();              // deduped flat union
        result.Topics.Should().HaveCount(2);                   // but retained under each topic
        result.Topics.Should().OnlyContain(t => t.Passages.Count == 1);
    }

    [Fact]
    public async Task No_topics_is_single_shot_with_empty_grouping()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Results(("Grappling", "PlayerHandbook 2014")));
        var svc = new RulesAdjudicationService(rag);

        var result = await svc.AskAsync("grappling", ruleTopics: null, edition: null, CancellationToken.None);

        result.Topics.Should().BeEmpty();
        result.Passages.Should().ContainSingle();
        await rag.Received(1).SearchAsync(
            Arg.Is<RetrievalQuery>(q => q.QueryText == "grappling" && q.TopK == RuleSources.TopK),
            Arg.Any<CancellationToken>());
    }
}
