using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Campaign;

public sealed class HeroRepositoryTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly HeroRepository _repo;
    private readonly long _campaignId;

    public HeroRepositoryTests()
    {
        var campRepo = new CampaignRepository(_db);
        _repo = new HeroRepository(_db);
        _campaignId = campRepo.CreateAsync(1, "Test Campaign", "").GetAwaiter().GetResult();
    }

    public void Dispose() => _db.Dispose();

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
        var sheet = new CharacterSheet { Level = 3, Class = "Rogue" };

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
        await _repo.SaveSnapshotAsync(id, 1, "Session 1", new CharacterSheet { Level = 1 });
        await _repo.SaveSnapshotAsync(id, 2, "Session 2", new CharacterSheet { Level = 2 });

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
        var sheet = new CharacterSheet { Level = 7, Race = "Elf", Class = "Ranger" };
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

        await _repo.DeleteAsync(id);

        var hero = await _repo.GetByIdAsync(id);
        hero.Should().BeNull();

        var snapshots = await _repo.GetSnapshotsAsync(id);
        snapshots.Should().BeEmpty();
    }
}
