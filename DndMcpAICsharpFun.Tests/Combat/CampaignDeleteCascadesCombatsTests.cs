using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Combat;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Tests.Combat;

[Collection("postgres")]
public sealed class CampaignDeleteCascadesCombatsTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CombatRepository _combats = new(new TestDb(pg));
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly UserRepository _users = new(new TestDb(pg));
    private readonly TestDb _db = new(pg);

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Deleting_a_campaign_removes_its_combats_and_combatants()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var combatId = (await _combats.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014))!.Value;
        await _combats.AddCombatantAsync(combatId, campaignId, userId,
            new Combatant { Name = "Goblin", MaxHp = 7, CurrentHp = 7 });

        await _campaigns.DeleteAsync(campaignId, userId);

        await using var db = _db.CreateDbContext();
        (await db.Combats.AnyAsync(c => c.CampaignId == campaignId)).Should().BeFalse();
        (await db.Combatants.AnyAsync(x => x.CombatId == combatId)).Should().BeFalse();
    }
}
