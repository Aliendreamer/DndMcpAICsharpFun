using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Tests.Persistence;

/// <summary>An <see cref="IDbContextFactory{AppDbContext}"/> over the shared Postgres test container.</summary>
public sealed class TestDb(PostgresFixture pg) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => pg.NewContext();
}
