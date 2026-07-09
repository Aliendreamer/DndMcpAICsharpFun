using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Dice;
using DndMcpAICsharpFun.Tests.Persistence;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Campaign;

[Collection("postgres")]
public sealed class CampaignLogRepositoryTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CampaignLogRepository _repo = new(new TestDb(pg));
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly UserRepository _users = new(new TestDb(pg));

    private static readonly RollResult RollFixture = new(
        DiceExpression.Parse("2d6+3"),
        new[] { 4, 5 },
        new[] { 4, 5 },
        3,
        12,
        "2d6+3 → [4,5]+3 = 12");

    private static readonly EncounterLogPayload EncounterFixture = new(
        "Deadly",
        500,
        750,
        new[] { 3, 3, 3, 3 },
        new[] { new EncounterMonsterLog("mm.monster.goblin", "Goblin", 0.25, 50) },
        true,
        null);

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(long user1, long campaign1, long user2)> SeedAsync()
    {
        var user1 = await _users.CreateAsync("owner", "hash-owner");
        var campaign1 = await _campaigns.CreateAsync(user1, "Owner's Campaign", "");
        var user2 = await _users.CreateAsync("intruder", "hash-intruder");
        await _campaigns.CreateAsync(user2, "Intruder's Campaign", "");
        return (user1, campaign1, user2);
    }

    [Fact]
    public async Task AddRoll_and_AddEncounter_are_returned_newest_first_with_payload_round_trip()
    {
        var (user1, campaign1, _) = await SeedAsync();

        var rollId = await _repo.AddRollAsync(user1, campaign1, RollFixture, "Deception");
        var encId = await _repo.AddEncounterAsync(user1, campaign1, EncounterFixture, "Boss fight", hidden: true);

        var entries = await _repo.GetByCampaignAsync(campaign1, user1);

        entries.Should().HaveCount(2);
        entries[0].Id.Should().Be(encId); // newest first
        entries[1].Id.Should().Be(rollId);

        var rollEntry = entries.Single(e => e.Id == rollId);
        rollEntry.Kind.Should().Be(CampaignLogKind.Roll);
        rollEntry.Label.Should().Be("Deception");
        rollEntry.Hidden.Should().BeFalse();
        var rollPayload = JsonSerializer.Deserialize<RollLogPayload>(rollEntry.PayloadJson);
        rollPayload.Should().NotBeNull();
        rollPayload!.Total.Should().Be(RollFixture.Total);
        rollPayload.Dice.Should().Equal(RollFixture.Dice);
        rollPayload.Breakdown.Should().Be(RollFixture.Breakdown);

        var encEntry = entries.Single(e => e.Id == encId);
        encEntry.Kind.Should().Be(CampaignLogKind.Encounter);
        encEntry.Label.Should().Be("Boss fight");
        encEntry.Hidden.Should().BeTrue();
        var encPayload = JsonSerializer.Deserialize<EncounterLogPayload>(encEntry.PayloadJson);
        encPayload.Should().NotBeNull();
        encPayload!.Difficulty.Should().Be(EncounterFixture.Difficulty);
        encPayload.Monsters.Should().BeEquivalentTo(EncounterFixture.Monsters);
    }

    [Fact]
    public async Task Foreign_user_cannot_see_or_modify_another_users_campaign_log()
    {
        var (user1, campaign1, user2) = await SeedAsync();

        var rollId = await _repo.AddRollAsync(user1, campaign1, RollFixture, "Deception");
        var encId = await _repo.AddEncounterAsync(user1, campaign1, EncounterFixture, "Boss fight", hidden: true);

        // Foreign user sees nothing.
        (await _repo.GetByCampaignAsync(campaign1, user2)).Should().BeEmpty();

        // Foreign user cannot reveal.
        await _repo.RevealAsync(encId, campaign1, user2);
        var stillHidden = (await _repo.GetByCampaignAsync(campaign1, user1)).Single(e => e.Id == encId);
        stillHidden.Hidden.Should().BeTrue();

        // Foreign user cannot delete.
        await _repo.DeleteAsync(rollId, campaign1, user2);
        (await _repo.GetByCampaignAsync(campaign1, user1)).Should().Contain(e => e.Id == rollId);

        // The owner can do both.
        await _repo.RevealAsync(encId, campaign1, user1);
        var revealed = (await _repo.GetByCampaignAsync(campaign1, user1)).Single(e => e.Id == encId);
        revealed.Hidden.Should().BeFalse();

        await _repo.DeleteAsync(rollId, campaign1, user1);
        (await _repo.GetByCampaignAsync(campaign1, user1)).Should().NotContain(e => e.Id == rollId);
    }

    [Fact]
    public async Task Deleting_campaign_cascades_campaign_log_entries()
    {
        var (user1, campaign1, _) = await SeedAsync();
        await _repo.AddRollAsync(user1, campaign1, RollFixture, "Deception");

        await _campaigns.DeleteAsync(campaign1, user1);

        (await _repo.GetByCampaignAsync(campaign1, user1)).Should().BeEmpty();
    }
}