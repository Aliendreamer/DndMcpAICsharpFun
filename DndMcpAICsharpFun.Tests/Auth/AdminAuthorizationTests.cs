using System.Security.Claims;

using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Tests.Persistence;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Tests.Auth;

[Collection("postgres")]
public sealed class AdminAuthorizationTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly UserRepository _repo = new(new TestDb(pg));
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static AuthorizationHandlerContext Ctx(params Claim[] claims) =>
        new([new AdminRequirement()], new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), null);

    private AdminAuthorizationHandler Handler() => new(_repo);

    private async Task SetAdminAsync(long id, bool value)
    {
        await using var db = pg.NewContext();
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, value));
    }

    [Fact]
    public async Task Handler_succeeds_for_admin()
    {
        var id = await _repo.CreateAsync("root", "h");
        await SetAdminAsync(id, true);
        var ctx = Ctx(new Claim(ClaimTypes.NameIdentifier, id.ToString()));

        await Handler().HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_fails_for_non_admin()
    {
        var id = await _repo.CreateAsync("bob", "h");
        var ctx = Ctx(new Claim(ClaimTypes.NameIdentifier, id.ToString()));

        await Handler().HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Handler_fails_after_flag_removed()
    {
        var id = await _repo.CreateAsync("root", "h");
        await SetAdminAsync(id, true);
        var handler = Handler();
        var first = Ctx(new Claim(ClaimTypes.NameIdentifier, id.ToString()));
        await handler.HandleAsync(first);
        first.HasSucceeded.Should().BeTrue();

        await SetAdminAsync(id, false);
        var second = Ctx(new Claim(ClaimTypes.NameIdentifier, id.ToString()));
        await handler.HandleAsync(second);

        second.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Handler_fails_without_name_identifier()
    {
        var ctx = Ctx(new Claim(ClaimTypes.Name, "root"));

        await Handler().HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeFalse();
    }
}
