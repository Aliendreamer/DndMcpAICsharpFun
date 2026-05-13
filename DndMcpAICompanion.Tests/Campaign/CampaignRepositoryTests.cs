using DndMcpAICompanion.Features.Campaign;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DndMcpAICompanion.Tests.Campaign;

public sealed class CampaignRepositoryTests : IAsyncLifetime
{
    private readonly string _connStr = $"Data Source=camp_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection _keepAlive = null!;
    private CampaignRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connStr);
        await _keepAlive.OpenAsync();
        _repo = new CampaignRepository(_connStr);
        await _repo.InitializeAsync();
    }

    public async Task DisposeAsync() => _keepAlive.Dispose();

    private async Task<long> ScalarAsync(string sql, Action<SqliteCommand> setup)
    {
        await using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = sql;
        setup(cmd);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task ExecAsync(string sql, Action<SqliteCommand> setup)
    {
        await using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = sql;
        setup(cmd);
        await cmd.ExecuteNonQueryAsync();
    }

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
        await ExecAsync(
            "INSERT INTO Heroes (CampaignId, Name, CreatedAt) VALUES (@c, 'Hero', @t)",
            cmd => { cmd.Parameters.AddWithValue("@c", id); cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O")); });

        await _repo.DeleteAsync(id, 1);

        (await _repo.GetAllAsync(1)).Should().BeEmpty();
        var heroCount = await ScalarAsync("SELECT COUNT(*) FROM Heroes WHERE CampaignId = @id",
            cmd => cmd.Parameters.AddWithValue("@id", id));
        heroCount.Should().Be(0);
    }

    [Fact]
    public async Task GetAll_IncludesHeroCount()
    {
        var id = await _repo.CreateAsync(1, "Party", "");
        await ExecAsync(
            "INSERT INTO Heroes (CampaignId, Name, CreatedAt) VALUES (@c, 'A', @t), (@c, 'B', @t)",
            cmd => { cmd.Parameters.AddWithValue("@c", id); cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O")); });

        var campaigns = await _repo.GetAllAsync(1);

        campaigns.Single().HeroCount.Should().Be(2);
    }
}
