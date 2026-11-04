using DndMcpAICsharpFun.Features.Auth;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Tests.Persistence;

[Collection("postgres")]
public sealed class UserIsAdminTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly UserRepository _repo = new(new TestDb(pg));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task IsAdmin_defaults_false()
    {
        await _repo.CreateAsync("alice", "hash");

        var user = await _repo.FindByUsernameAsync("alice");

        user!.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task IsAdminAsync_reads_flag()
    {
        var id = await _repo.CreateAsync("alice", "hash");
        (await _repo.IsAdminAsync(id)).Should().BeFalse();

        await using var db = pg.NewContext();
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, true));

        (await _repo.IsAdminAsync(id)).Should().BeTrue();
        (await _repo.IsAdminAsync(id + 999)).Should().BeFalse();
    }
}
