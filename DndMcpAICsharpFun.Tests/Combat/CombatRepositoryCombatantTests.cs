using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Combat;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

[Collection("postgres")]
public sealed class CombatRepositoryCombatantTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CombatRepository _repo = new(new TestDb(pg));
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly UserRepository _users = new(new TestDb(pg));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(long userId, long campaignId, long combatId)> SeedCombatAsync()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var combatId = (await _repo.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014))!.Value;
        return (userId, campaignId, combatId);
    }

    private static Combatant Monster(string name, int init) =>
        new() { Name = name, IsPlayer = false, InitiativeRoll = init, MaxHp = 7, CurrentHp = 7 };

    [Fact]
    public async Task Add_update_and_read_combatant_persists_hp_and_conditions()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var id = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("Goblin", 12));

        await _repo.UpdateCombatantAsync(id, combatId, campaignId, userId,
            currentHp: 2, initiativeRoll: 15, initiativeModifier: 2,
            conditions: new[] { Condition.Poisoned, Condition.Prone });

        var combatants = await _repo.GetCombatantsAsync(combatId, campaignId, userId);
        var goblin = combatants.Single();
        goblin.CurrentHp.Should().Be(2);
        goblin.InitiativeRoll.Should().Be(15);
        goblin.Conditions.Should().Equal(Condition.Poisoned, Condition.Prone);
    }

    [Fact]
    public async Task Advance_turn_wraps_and_increments_round()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));
        await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("B", 10));

        await _repo.AdvanceTurnAsync(combatId, campaignId, userId); // 0 -> 1
        var mid = await _repo.GetByIdAsync(combatId, campaignId, userId);
        mid!.CurrentTurnIndex.Should().Be(1);
        mid.Round.Should().Be(1);

        await _repo.AdvanceTurnAsync(combatId, campaignId, userId); // 1 -> wrap to 0, round 2
        var wrapped = await _repo.GetByIdAsync(combatId, campaignId, userId);
        wrapped!.CurrentTurnIndex.Should().Be(0);
        wrapped.Round.Should().Be(2);
    }

    [Fact]
    public async Task Remove_combatant_deletes_only_that_row()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var a = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));
        await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("B", 10));

        await _repo.RemoveCombatantAsync(a, combatId, campaignId, userId);

        (await _repo.GetCombatantsAsync(combatId, campaignId, userId)).Should().ContainSingle(c => c.Name == "B");
    }

    [Fact]
    public async Task Deleting_a_combat_removes_its_combatants()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));

        await _repo.DeleteAsync(combatId, campaignId, userId);

        (await _repo.GetByIdAsync(combatId, campaignId, userId)).Should().BeNull();
        (await _repo.GetCombatantsAsync(combatId, campaignId, userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Another_users_combat_cannot_be_mutated()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var combatantId = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));
        var intruder = await _users.CreateAsync("intruder", "hash");

        // Wrong user on every command → no effect.
        (await _repo.AddCombatantAsync(combatId, campaignId, intruder, Monster("X", 1))).Should().Be(0);
        await _repo.UpdateCombatantAsync(combatantId, combatId, campaignId, intruder, 0, 1, 0, Array.Empty<Condition>());
        await _repo.AdvanceTurnAsync(combatId, campaignId, intruder);
        await _repo.EndAsync(combatId, campaignId, intruder);
        await _repo.RemoveCombatantAsync(combatantId, combatId, campaignId, intruder);
        await _repo.DeleteAsync(combatId, campaignId, intruder);

        var combat = await _repo.GetByIdAsync(combatId, campaignId, userId);
        combat.Should().NotBeNull();
        combat!.Status.Should().Be(CombatStatus.Active);
        combat.CurrentTurnIndex.Should().Be(0);
        var combatants = await _repo.GetCombatantsAsync(combatId, campaignId, userId);
        combatants.Should().ContainSingle(c => c.Id == combatantId && c.CurrentHp == 7);
    }
}
