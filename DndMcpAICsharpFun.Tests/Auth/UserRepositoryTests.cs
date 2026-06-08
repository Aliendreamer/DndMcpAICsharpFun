using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Auth;

[Collection("postgres")]
public sealed class UserRepositoryTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly UserRepository _repo = new(new TestDb(pg));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

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

    [Fact]
    public async Task ExistsAsync_ReflectsCreatedUsers()
    {
        (await _repo.ExistsAsync("carol")).Should().BeFalse();
        await _repo.CreateAsync("carol", "hash-c");
        (await _repo.ExistsAsync("carol")).Should().BeTrue();
    }
}
