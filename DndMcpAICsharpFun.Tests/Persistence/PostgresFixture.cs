using DndMcpAICsharpFun.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Respawn;
using Respawn.Graph;

using Testcontainers.PostgreSql;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Persistence;

/// <summary>
/// Starts one PostgreSQL container shared across the "postgres" test collection,
/// applies migrations, and exposes a Respawn-based reset so each test starts clean.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } =
        new PostgreSqlBuilder("postgres:18-alpine").Build();

    private Respawner _respawner = null!;

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        await using var conn = new NpgsqlConnection(Container.GetConnectionString());
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Table("__EFMigrationsHistory")],
        });
    }

    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Container.GetConnectionString()).Options);

    /// <summary>Reset all tables (keeps the schema/migrations). Call before each test.</summary>
    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(Container.GetConnectionString());
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }

    public async Task DisposeAsync() => await Container.DisposeAsync();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;