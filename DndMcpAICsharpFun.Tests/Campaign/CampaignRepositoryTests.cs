using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Campaign;

public sealed class CampaignRepositoryTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly CampaignRepository _repo;
    private readonly HeroRepository _heroes;

    public CampaignRepositoryTests()
    {
        _repo = new CampaignRepository(_db);
        _heroes = new HeroRepository(_db);
    }

    public void Dispose() => _db.Dispose();

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
