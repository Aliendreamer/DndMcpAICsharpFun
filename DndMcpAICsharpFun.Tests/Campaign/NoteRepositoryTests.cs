using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Tests.Persistence;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Campaign;

[Collection("postgres")]
public sealed class NoteRepositoryTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly NoteRepository _repo = new(new TestDb(pg));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_then_GetByCampaign_returns_the_note()
    {
        var id = await _repo.CreateAsync(userId: 1, campaignId: 10, "Session 1", "Party met the innkeeper.");

        var notes = await _repo.GetByCampaignAsync(10);

        notes.Should().ContainSingle();
        notes[0].Id.Should().Be(id);
        notes[0].Title.Should().Be("Session 1");
        notes[0].Content.Should().Be("Party met the innkeeper.");
    }

    [Fact]
    public async Task GetByCampaign_is_scoped_and_newest_first()
    {
        await _repo.CreateAsync(1, 10, "A", "first");
        await _repo.CreateAsync(1, 10, "B", "second");
        await _repo.CreateAsync(1, 99, "other", "other campaign");

        var notes = await _repo.GetByCampaignAsync(10);

        notes.Should().HaveCount(2);
        notes[0].Title.Should().Be("B"); // most recent first
    }

    [Fact]
    public async Task Update_changes_title_and_content()
    {
        var id = await _repo.CreateAsync(1, 10, "t", "c");

        await _repo.UpdateAsync(id, 10, "t2", "c2");

        var note = (await _repo.GetByCampaignAsync(10)).Single();
        note.Title.Should().Be("t2");
        note.Content.Should().Be("c2");
    }

    [Fact]
    public async Task Delete_removes_the_note()
    {
        var id = await _repo.CreateAsync(1, 10, "t", "c");

        await _repo.DeleteAsync(id, 10);

        (await _repo.GetByCampaignAsync(10)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_the_campaign_removes_its_notes()
    {
        var campaigns = new CampaignRepository(new TestDb(pg));
        var campaignId = await campaigns.CreateAsync(userId: 1, "C", "");
        await _repo.CreateAsync(1, campaignId, "n", "note");

        await campaigns.DeleteAsync(campaignId, 1);

        (await _repo.GetByCampaignAsync(campaignId)).Should().BeEmpty();
    }
}