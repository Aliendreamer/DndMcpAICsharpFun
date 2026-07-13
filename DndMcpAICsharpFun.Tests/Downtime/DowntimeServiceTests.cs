using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Downtime;
using DndMcpAICsharpFun.Features.Retrieval;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Downtime;

public sealed class DowntimeServiceTests
{
    private static List<RetrievalResult> Results(params (string text, string book)[] rows) =>
        rows.Select(r => new RetrievalResult(
            r.text,
            new ChunkMetadata(
                SourceBook: r.book,
                Version: DndVersion.Edition2014,
                Category: ContentCategory.Rule,
                EntityName: null,
                Chapter: "Downtime",
                PageNumber: 0,
                ChunkIndex: 0,
                SectionTitle: "Crafting"),
            Score: 0.9f)).ToList();

    [Fact]
    public async Task Scopes_retrieval_to_the_downtime_books_at_higher_topK()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Results(("Crafting: ...", "Xanathar's Guide to Everything")));
        var svc = new DowntimeService(rag);

        await svc.PlanAsync("craft plate armor", edition: null, CancellationToken.None);

        await rag.Received().SearchAsync(
            Arg.Is<RetrievalQuery>(q => q.SourceKeys != null
                && q.SourceKeys.Contains("XGE")
                && q.SourceKeys.Contains("DMG")
                && q.TopK == DowntimeSources.TopK),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Projects_results_to_cited_passages()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Results(("Crafting rule text", "Xanathar's Guide to Everything")));
        var svc = new DowntimeService(rag);

        var result = await svc.PlanAsync("crafting", null, CancellationToken.None);

        result.Passages.Should().ContainSingle();
        result.Passages[0].SourceBook.Should().Be("Xanathar's Guide to Everything");
    }

    [Fact]
    public async Task Empty_retrieval_returns_explicit_empty()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>()).Returns(new List<RetrievalResult>());
        (await new DowntimeService(rag).PlanAsync("nonsense", null, CancellationToken.None)).Passages.Should().BeEmpty();
    }
}
