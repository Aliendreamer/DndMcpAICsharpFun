using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Tests.Persistence;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Lore;

[Collection("postgres")]
public sealed class SettingLoreServiceTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static RetrievalResult CannedResult() => new(
        "The Dragonmarked Houses are twelve noble bloodlines of Khorvaire.",
        new ChunkMetadata(
            SourceBook: "ERLW",
            Version: DndVersion.Edition2014,
            Category: ContentCategory.Lore,
            EntityName: null,
            Chapter: "Chapter 1",
            PageNumber: 12,
            ChunkIndex: 0,
            SectionTitle: "Dragonmarked Houses"),
        Score: 0.87f);

    [Fact]
    public async Task Scopes_retrieval_to_the_campaigns_setting_books()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns([CannedResult()]);
        var id = await _campaigns.CreateAsync(1, "Eberron", "d", setting: "Eberron");
        var svc = new SettingLoreService(_campaigns, rag);

        await svc.AskForUserAsync(1, id, "Dragonmarked Houses", version: null, CancellationToken.None);

        await rag.Received().SearchAsync(
            Arg.Is<RetrievalQuery>(q => q.SourceKeys != null
                && q.SourceKeys.Contains("ERLW") && q.SourceKeys.Contains("PHB")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Generic_campaign_scopes_to_nothing_unscoped()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns([CannedResult()]);
        var id = await _campaigns.CreateAsync(1, "Generic", "d"); // no setting
        var svc = new SettingLoreService(_campaigns, rag);

        await svc.AskForUserAsync(1, id, "fireball", version: null, CancellationToken.None);

        await rag.Received().SearchAsync(
            Arg.Is<RetrievalQuery>(q => q.SourceKeys == null || q.SourceKeys.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Foreign_campaign_throws_and_does_not_query()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        var id = await _campaigns.CreateAsync(2, "Other", "d", setting: "Eberron"); // owned by user 2
        var svc = new SettingLoreService(_campaigns, rag);

        var act = () => svc.AskForUserAsync(1, id, "q", version: null, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await rag.DidNotReceive().SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Empty_retrieval_returns_an_explicit_empty_result()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var id = await _campaigns.CreateAsync(1, "E", "d", setting: "Eberron");
        var svc = new SettingLoreService(_campaigns, rag);

        var result = await svc.AskForUserAsync(1, id, "nonsense", version: null, CancellationToken.None);

        result.Passages.Should().BeEmpty();
    }
}
