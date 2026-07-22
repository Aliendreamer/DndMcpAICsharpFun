using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Tests.Persistence;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Campaign;

[Collection("postgres")]
public sealed class HeroRepositoryTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly HeroRepository _repo = new(new TestDb(pg));
    private long _campaignId;

    public async Task InitializeAsync()
    {
        await pg.ResetAsync();
        var campRepo = new CampaignRepository(new TestDb(pg));
        _campaignId = await campRepo.CreateAsync(1, "Test Campaign", "");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateAsync_InsertsHeroWithBlankSnapshot()
    {
        var id = await _repo.CreateAsync(_campaignId, "Gandalf");

        var hero = await _repo.GetByIdAsync(id);

        hero.Should().NotBeNull();
        hero!.Name.Should().Be("Gandalf");
        hero.LatestSnapshot.Should().NotBeNull();
        hero.LatestSnapshot!.SessionNumber.Should().Be(0);
    }

    [Fact]
    public async Task SaveSnapshotAsync_AddsNewSnapshot()
    {
        var id = await _repo.CreateAsync(_campaignId, "Frodo");
        var sheet = new CharacterSheet { Classes = [new ClassLevel { Class = "Rogue", Level = 3 }] };

        await _repo.SaveSnapshotAsync(id, 2, "After Moria", sheet);

        var hero = await _repo.GetByIdAsync(id);
        hero!.LatestSnapshot!.SessionNumber.Should().Be(2);
        hero.LatestSnapshot.SessionLabel.Should().Be("After Moria");
        hero.LatestSnapshot.Sheet.Level.Should().Be(3);
        hero.LatestSnapshot.Sheet.Class.Should().Be("Rogue");
    }

    [Fact]
    public async Task GetSnapshotsAsync_ReturnsAllInDescendingOrder()
    {
        var id = await _repo.CreateAsync(_campaignId, "Aragorn");
        await _repo.SaveSnapshotAsync(id, 1, "Session 1", new CharacterSheet { Classes = [new ClassLevel { Level = 1 }] });
        await _repo.SaveSnapshotAsync(id, 2, "Session 2", new CharacterSheet { Classes = [new ClassLevel { Level = 2 }] });

        var snapshots = await _repo.GetSnapshotsAsync(id);

        // 3 total: initial blank + 2 saved; latest first
        snapshots.Should().HaveCount(3);
        snapshots[0].SessionNumber.Should().Be(2);
        snapshots[1].SessionNumber.Should().Be(1);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReturnsFullSheet()
    {
        var id = await _repo.CreateAsync(_campaignId, "Legolas");
        var sheet = new CharacterSheet { Race = "Elf", Classes = [new ClassLevel { Class = "Ranger", Level = 7 }] };
        await _repo.SaveSnapshotAsync(id, 3, "Helm's Deep", sheet);

        var snapshots = await _repo.GetSnapshotsAsync(id);
        var full = await _repo.GetSnapshotAsync(snapshots[0].Id);

        full.Should().NotBeNull();
        full!.Sheet.Level.Should().Be(7);
        full.Sheet.Race.Should().Be("Elf");
    }

    [Fact]
    public async Task GetByCampaignAsync_ReturnsAllHeroesWithLatestSnapshot()
    {
        await _repo.CreateAsync(_campaignId, "Hero A");
        await _repo.CreateAsync(_campaignId, "Hero B");

        var heroes = await _repo.GetByCampaignAsync(_campaignId);

        heroes.Should().HaveCount(2);
        heroes.All(h => h.LatestSnapshot != null).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_RemovesHeroAndSnapshots()
    {
        var id = await _repo.CreateAsync(_campaignId, "Boromir");
        await _repo.SaveSnapshotAsync(id, 1, "S1", new CharacterSheet());

        await _repo.DeleteAsync(id, 1);

        var hero = await _repo.GetByIdAsync(id);
        hero.Should().BeNull();

        var snapshots = await _repo.GetSnapshotsAsync(id);
        snapshots.Should().BeEmpty();
    }


    // Task 4.3 (audit P3): DeleteAsync now takes an owning userId scope (mirrors
    // CampaignRepository.DeleteAsync) instead of deleting by bare hero id.
    [Fact]
    public async Task DeleteAsync_ForAnotherUser_IsNoOp()
    {
        var id = await _repo.CreateAsync(_campaignId, "Samwise");

        await _repo.DeleteAsync(id, userId: 999);

        var hero = await _repo.GetByIdAsync(id);
        hero.Should().NotBeNull("a non-owning userId must not be able to delete another user's hero");
    }
}