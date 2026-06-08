using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Campaign;

[Collection("postgres")]
public sealed class CampaignRepositoryTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CampaignRepository _repo = new(new TestDb(pg));
    private readonly HeroRepository _heroes = new(new TestDb(pg));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateAndGetAll_ReturnsOnlyUserCampaigns()
    {
        await _repo.CreateAsync(1, "Campaign A", "desc");
        await _repo.CreateAsync(1, "Campaign B", "");
        await _repo.CreateAsync(2, "Other User", "");

        var campaigns = await _repo.GetAllAsync(1);

        campaigns.Should().HaveCount(2);
        campaigns.Select(c => c.Name).Should().BeEquivalentTo(["Campaign A", "Campaign B"]);
    }

    [Fact]
    public async Task GetById_ReturnsNull_ForWrongUser()
    {
        var id = await _repo.CreateAsync(1, "Secret", "");

        var result = await _repo.GetByIdAsync(id, 2);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetById_ReturnsCorrectFields()
    {
        var id = await _repo.CreateAsync(1, "My Campaign", "D&D adventure");

        var result = await _repo.GetByIdAsync(id, 1);

        result.Should().NotBeNull();
        result!.Name.Should().Be("My Campaign");
        result.Description.Should().Be("D&D adventure");
    }

    [Fact]
    public async Task Delete_RemovesCampaignAndHeroes()
    {
        var id = await _repo.CreateAsync(1, "ToDelete", "");
        await _heroes.CreateAsync(id, "Hero");

        await _repo.DeleteAsync(id, 1);

        (await _repo.GetAllAsync(1)).Should().BeEmpty();
        (await _heroes.GetByCampaignAsync(id)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAll_IncludesHeroCount()
    {
        var id = await _repo.CreateAsync(1, "Party", "");
        await _heroes.CreateAsync(id, "A");
        await _heroes.CreateAsync(id, "B");

        var campaigns = await _repo.GetAllAsync(1);

        campaigns.Single().HeroCount.Should().Be(2);
    }
}
