using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Combat;
using DndMcpAICsharpFun.Features.Dice;
using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

/// <summary>A deterministic RNG that always returns the low end of the range (d20 → 1).</summary>
file sealed class MinRandomSource : IRandomSource
{
    public int Next(int minInclusive, int maxExclusive) => minInclusive;
}

[Collection("postgres")]
public sealed class CombatServiceDraftingTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CombatRepository _combats = new(new TestDb(pg));
    private readonly HeroRepository _heroes = new(new TestDb(pg));
    private readonly CampaignLogRepository _log = new(new TestDb(pg));
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly UserRepository _users = new(new TestDb(pg));

    private CombatService NewService() =>
        new(_combats, _heroes, _log, new DiceRoller(new MinRandomSource()));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Draft_party_creates_player_combatants_from_hero_sheets()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var heroId = await _heroes.CreateAsync(campaignId, "Aria");
        await _heroes.SaveSnapshotAsync(heroId, 1, "S1",
            new CharacterSheet { MaxHitPoints = 12, CurrentHitPoints = 9, ArmorClass = 15 });
        var combatId = (await _combats.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014))!.Value;

        await NewService().DraftPartyAsync(combatId, campaignId, userId);

        var combatants = await _combats.GetCombatantsAsync(combatId, campaignId, userId);
        var aria = combatants.Single();
        aria.IsPlayer.Should().BeTrue();
        aria.HeroId.Should().Be(heroId);
        aria.Name.Should().Be("Aria");
        aria.MaxHp.Should().Be(12);
        aria.CurrentHp.Should().Be(9);
        aria.Ac.Should().Be(15);
        aria.InitiativeRoll.Should().BeNull();
    }

    [Fact]
    public async Task Draft_monsters_auto_rolls_initiative_over_the_injected_rng()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var combatId = (await _combats.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014))!.Value;

        await NewService().DraftMonstersAsync(combatId, campaignId, userId,
            new[] { new MonsterRef("mm.monster.goblin", "Goblin", 0.25, 50) });

        var goblin = (await _combats.GetCombatantsAsync(combatId, campaignId, userId)).Single();
        goblin.IsPlayer.Should().BeFalse();
        goblin.InitiativeRoll.Should().Be(1); // MinRandomSource → d20 = 1, + modifier 0
        goblin.Name.Should().Be("Goblin");
    }


    [Fact]
    public async Task Draft_monsters_applies_the_monster_initiative_modifier()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var combatId = (await _combats.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014))!.Value;

        // MonsterRef with InitiativeModifier +3; MinRandomSource → d20 = 1, so init = 1 + 3 = 4.
        await NewService().DraftMonstersAsync(combatId, campaignId, userId,
            new[] { new MonsterRef("mm.monster.ogre", "Ogre", 2, 450, 3) });

        var ogre = (await _combats.GetCombatantsAsync(combatId, campaignId, userId)).Single();
        ogre.InitiativeModifier.Should().Be(3);
        ogre.InitiativeRoll.Should().Be(4);
    }
}
