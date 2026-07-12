using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Persistence;

[Collection("postgres")]
public sealed class CampaignSettingTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CampaignRepository _repo = new(new TestDb(pg));
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_with_setting_round_trips()
    {
        var id = await _repo.CreateAsync(1, "Eberron Game", "desc", setting: "Eberron");
        (await _repo.GetByIdAsync(id, 1))!.Setting.Should().Be("Eberron");
    }

    [Fact]
    public async Task Create_without_setting_defaults_null()
    {
        var id = await _repo.CreateAsync(1, "Generic", "desc");
        (await _repo.GetByIdAsync(id, 1))!.Setting.Should().BeNull();
    }

    [Fact]
    public async Task SetSettingAsync_updates_owned_campaign_and_ignores_foreign()
    {
        var id = await _repo.CreateAsync(1, "C", "d");
        await _repo.SetSettingAsync(id, 1, "Eberron");
        (await _repo.GetByIdAsync(id, 1))!.Setting.Should().Be("Eberron");

        await _repo.SetSettingAsync(id, userId: 2, "Ravenloft"); // foreign — no-op
        (await _repo.GetByIdAsync(id, 1))!.Setting.Should().Be("Eberron");
    }
}
