using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Combat;
using DndMcpAICsharpFun.Tests.Persistence;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

[Collection("postgres")]
public sealed class CombatRepositoryLifecycleTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CombatRepository _repo = new(new TestDb(pg));
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly UserRepository _users = new(new TestDb(pg));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(long userId, long campaignId)> SeedAsync()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        return (userId, campaignId);
    }

    [Fact]
    public async Task Start_creates_active_combat_at_round_one()
    {
        var (userId, campaignId) = await SeedAsync();

        var id = await _repo.StartAsync(userId, campaignId, "Goblins", DndVersion.Edition2014);

        id.Should().NotBeNull();
        var active = await _repo.GetActiveAsync(campaignId, userId);
        active.Should().NotBeNull();
        active!.Id.Should().Be(id!.Value);
        active.Status.Should().Be(CombatStatus.Active);
        active.Round.Should().Be(1);
        active.Name.Should().Be("Goblins");
    }

    [Fact]
    public async Task Start_rejects_a_second_active_combat()
    {
        var (userId, campaignId) = await SeedAsync();
        await _repo.StartAsync(userId, campaignId, "First", DndVersion.Edition2014);

        var second = await _repo.StartAsync(userId, campaignId, "Second", DndVersion.Edition2014);

        second.Should().BeNull();
        (await _repo.GetHistoryAsync(campaignId, userId)).Should().BeEmpty(); // none ended
    }

    [Fact]
    public async Task End_moves_combat_to_history_and_clears_active()
    {
        var (userId, campaignId) = await SeedAsync();
        var id = await _repo.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014);

        await _repo.EndAsync(id!.Value, campaignId, userId);

        (await _repo.GetActiveAsync(campaignId, userId)).Should().BeNull();
        var history = await _repo.GetHistoryAsync(campaignId, userId);
        history.Should().ContainSingle(c => c.Id == id.Value && c.Status == CombatStatus.Ended);
    }

    [Fact]
    public async Task Start_allows_a_new_combat_after_the_previous_one_ended()
    {
        var (userId, campaignId) = await SeedAsync();
        var first = await _repo.StartAsync(userId, campaignId, "First", DndVersion.Edition2014);
        await _repo.EndAsync(first!.Value, campaignId, userId);

        var second = await _repo.StartAsync(userId, campaignId, "Second", DndVersion.Edition2014);

        second.Should().NotBeNull();
        (await _repo.GetActiveAsync(campaignId, userId))!.Name.Should().Be("Second");
    }
}
