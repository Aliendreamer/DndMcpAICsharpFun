using DndMcpAICompanion.Features.Auth;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DndMcpAICompanion.Tests.Auth;

public sealed class UserRepositoryTests : IAsyncLifetime
{
    private readonly string _connStr = $"Data Source=users_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection _keepAlive = null!;
    private UserRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connStr);
        await _keepAlive.OpenAsync();
        _repo = new UserRepository(_connStr);
        await _repo.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        _keepAlive.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateAsync_ReturnsNewUserId_MatchingStoredRecord()
    {
        var id = await _repo.CreateAsync("alice", "hash-a");

        var stored = await _repo.FindByUsernameAsync("alice");
        stored.Should().NotBeNull();
        id.Should().Be(stored!.Id);
    }

    [Fact]
    public async Task CreateAsync_ReturnsDistinctIdsForDistinctUsers()
    {
        var first = await _repo.CreateAsync("alice", "hash-a");
        var second = await _repo.CreateAsync("bob", "hash-b");

        second.Should().NotBe(first);
    }
}
