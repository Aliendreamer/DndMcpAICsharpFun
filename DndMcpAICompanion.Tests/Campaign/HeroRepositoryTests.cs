using System.Text.Json;
using DndMcpAICompanion.Features.Campaign;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DndMcpAICompanion.Tests.Campaign;

public sealed class HeroRepositoryTests : IAsyncLifetime
{
    private readonly string _connStr = $"Data Source=hero_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection _keepAlive = null!;
    private CampaignRepository _campRepo = null!;
    private HeroRepository _repo = null!;
    private long _campaignId;

    public async Task InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connStr);
        await _keepAlive.OpenAsync();
        _campRepo = new CampaignRepository(_connStr);
        await _campRepo.InitializeAsync();
        _repo = new HeroRepository(_connStr);
        _campaignId = await _campRepo.CreateAsync(1, "Test Campaign", "");
    }

    public async Task DisposeAsync() => _keepAlive.Dispose();

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
