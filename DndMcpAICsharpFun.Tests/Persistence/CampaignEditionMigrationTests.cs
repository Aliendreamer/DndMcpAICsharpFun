using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Infrastructure.Persistence;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Persistence;

/// <summary>
/// Tasks 3.2/3.3 of <c>campaign-edition-and-book-allowlist</c> — replays the
/// <c>AddCampaignEditionAndBooks</c> migration (<see cref="MigrationUnderTest"/>) against a database
/// frozen at the migration immediately BEFORE it, to prove the migration preserves an existing
/// campaign's book access instead of silently narrowing it to the new-campaign core-only default.
///
/// Isolation: the shared "postgres" collection's <see cref="PostgresFixture"/> already has ALL
/// migrations applied (its <c>InitializeAsync</c> runs a full <c>MigrateAsync()</c> once), and Respawn
/// only resets DATA between tests — it never re-applies migrations over existing rows. You cannot
/// migrate that shared database back down to replay this migration without corrupting the schema for
/// every other test in the collection. Instead, each test here creates a throwaway database ON THE
/// SAME running container (cheap: no second Testcontainers spin-up), migrates only that throwaway
/// database to the PRECEDING migration, inserts a pre-migration-shaped row with raw SQL (the CURRENT
/// EF model already has the Edition/AllowedBookKeys columns, so a normal DbSet insert would not
/// reproduce the pre-migration row shape), applies the migration under test, and reads the result back.
/// </summary>
[Collection("postgres")]
public sealed class CampaignEditionMigrationTests(PostgresFixture pg) : IAsyncLifetime
{
    // The migration immediately before AddCampaignEditionAndBooks (see Migrations/ directory listing).
    private const string PreviousMigration = "20260722180055_AuditFkIndexes";
    private const string MigrationUnderTest = "20260727164652_AddCampaignEditionAndBooks";

    private string _databaseName = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _databaseName = $"campaign_migration_test_{Guid.NewGuid():N}";

        await using var admin = new NpgsqlConnection(pg.Container.GetConnectionString());
        await admin.OpenAsync();
        await using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(pg.Container.GetConnectionString())
        {
            Database = _databaseName,
        };
        _connectionString = builder.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(pg.Container.GetConnectionString());
        await admin.OpenAsync();
        await using var drop = admin.CreateCommand();
        // WITH (FORCE) (PG13+) disconnects any pooled connections still attached to the throwaway
        // database, so a lingering Npgsql pool connection can't block the drop.
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        await drop.ExecuteNonQueryAsync();
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options);

    [Fact]
    public async Task Migration_preserves_the_full_pre_migration_book_access_for_an_existing_campaign()
    {
        const string name = "Pre-migration campaign (3.2)";

        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync(PreviousMigration);
            await InsertPreMigrationCampaignAsync(db, name);
            await db.Database.MigrateAsync(MigrationUnderTest);
        }

        await using var verify = NewContext();
        var campaign = await verify.Campaigns.SingleAsync(c => c.Name == name);

        campaign.Edition.Should().Be(DndVersion.Edition2014,
            "the migration seeds Edition2014 for rows that predate the Edition column");

        // Assert BEHAVIOUR PRESERVED, not a magic count: every book the migration's seed SQL lists —
        // in particular the setting/supplement books — must still be reachable, or this is exactly the
        // silent-narrowing regression the migration exists to prevent.
        campaign.AllowedBookKeys.Should().BeEquivalentTo(
            ["PHB", "MM", "DMG", "XGE", "TCE", "ERLW", "VRGR", "FTD", "BGG", "EGW", "SCC", "GGR", "SCAG", "MTF", "MPMM", "EEPC"],
            "an existing campaign must keep exactly the books it could reach before this migration");

        campaign.AllowedBookKeys.Should().Contain(["ERLW", "XGE", "TCE", "SCAG"],
            "setting/supplement books are the ones a core-only regression would silently drop first");
    }

    [Fact]
    public async Task Migration_does_not_retroactively_apply_the_core_only_default_to_an_existing_campaign()
    {
        const string name = "Pre-migration campaign (3.3)";

        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync(PreviousMigration);
            await InsertPreMigrationCampaignAsync(db, name);
            await db.Database.MigrateAsync(MigrationUnderTest);
        }

        await using var verify = NewContext();
        var campaign = await verify.Campaigns.SingleAsync(c => c.Name == name);

        // Discriminating check: CampaignScopeResolver.CoreBooksFor(Edition2014) is the exact three-book
        // set ["PHB","DMG","MM"] that a NEW campaign gets by default. If someone later "simplifies" the
        // migration's seed SQL to reuse that default instead of the full pre-migration set, this test
        // must fail — both assertions below only pass because the migrated row carries the full
        // 16-book list, not the 3-book new-campaign default.
        var coreOnly = CampaignScopeResolver.CoreBooksFor(DndVersion.Edition2014);
        campaign.AllowedBookKeys.Should().NotBeEquivalentTo(coreOnly,
            "the core-only new-campaign default must never be applied retroactively to a pre-existing row");
        campaign.AllowedBookKeys.Should().HaveCountGreaterThan(coreOnly.Count,
            "an existing campaign keeps its full pre-migration book list, which is larger than the new core-only default");
    }

    /// <summary>
    /// Inserts a row shaped like the table BEFORE this migration (see
    /// Migrations/20260722180055_AuditFkIndexes.Designer.cs): no <c>Edition</c> or
    /// <c>AllowedBookKeys</c> columns exist yet at this point in the migration timeline. Raw SQL is
    /// required because the CURRENT EF model already includes those columns, so a normal
    /// <c>db.Campaigns.Add(...)</c> would not reproduce the pre-migration shape this test needs.
    /// </summary>
    private static async Task InsertPreMigrationCampaignAsync(AppDbContext db, string name) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Campaigns" ("UserId", "Name", "Description", "CreatedAt")
            VALUES (1, {name}, 'pre-migration row', now())
            """);
}
