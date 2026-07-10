using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Combat;
using DndMcpAICsharpFun.Features.Dice;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

[Collection("postgres")]
public sealed class CombatServiceEndTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CombatRepository _combats = new(new TestDb(pg));
    private readonly HeroRepository _heroes = new(new TestDb(pg));
    private readonly CampaignLogRepository _log = new(new TestDb(pg));
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly UserRepository _users = new(new TestDb(pg));

    private CombatService NewService() =>
        new(_combats, _heroes, _log, new DiceRoller(new SystemRandomSource()));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EndCombat_writes_back_approved_hp_and_drops_breadcrumb()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var heroId = await _heroes.CreateAsync(campaignId, "Aria");
        await _heroes.SaveSnapshotAsync(heroId, 1, "S1",
            new CharacterSheet { MaxHitPoints = 12, CurrentHitPoints = 12, ArmorClass = 15 });
        var combatId = (await _combats.StartAsync(userId, campaignId, "Goblin Ambush", DndVersion.Edition2014))!.Value;
        await NewService().DraftPartyAsync(combatId, campaignId, userId);
        var aria = (await _combats.GetCombatantsAsync(combatId, campaignId, userId)).Single();

        await NewService().EndCombatAsync(combatId, campaignId, userId,
            new Dictionary<long, int> { [aria.Id] = 4 });

        // A NEW snapshot is appended with the approved HP; the original snapshot is preserved.
        var snapshots = await _heroes.GetSnapshotsAsync(heroId);
        snapshots.Should().HaveCountGreaterThanOrEqualTo(2);
        var hero = await _heroes.GetByIdAsync(heroId);
        hero!.LatestSnapshot!.Sheet.CurrentHitPoints.Should().Be(4);

        // The combat is ended and a breadcrumb is in the log.
        (await _combats.GetActiveAsync(campaignId, userId)).Should().BeNull();
        var entries = await _log.GetByCampaignAsync(campaignId, userId);
        entries.Should().ContainSingle(e => e.Kind == CampaignLogKind.Combat);
    }

    [Fact]
    public async Task EndCombat_does_not_write_back_for_a_combatant_without_a_hero()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var combatId = (await _combats.StartAsync(userId, campaignId, "Brawl", DndVersion.Edition2014))!.Value;
        await NewService().AddManualAsync(combatId, campaignId, userId, "Thug", 11, 12, isPlayer: true, initiativeModifier: 0);
        var thug = (await _combats.GetCombatantsAsync(combatId, campaignId, userId)).Single();

        await NewService().EndCombatAsync(combatId, campaignId, userId,
            new Dictionary<long, int> { [thug.Id] = 0 });

        (await _combats.GetActiveAsync(campaignId, userId)).Should().BeNull(); // still ends
        var entries = await _log.GetByCampaignAsync(campaignId, userId);
        entries.Should().ContainSingle(e => e.Kind == CampaignLogKind.Combat);
    }
}
