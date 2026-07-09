using DndMcpAICsharpFun.Domain;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Tests.Persistence;

[Collection("postgres")]
public sealed class AppDbContextSmokeTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task All_DbSets_are_queryable_on_a_fresh_migrated_database()
    {
        await using var db = pg.NewContext();
        (await db.Users.CountAsync()).Should().Be(0);
        (await db.Campaigns.CountAsync()).Should().Be(0);
        (await db.Heroes.CountAsync()).Should().Be(0);
        (await db.HeroSnapshots.CountAsync()).Should().Be(0);
        (await db.ChatTurns.CountAsync()).Should().Be(0);
        (await db.IngestionRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task HeroSnapshot_CharacterSheet_round_trips_through_the_json_column()
    {
        await using (var db = pg.NewContext())
        {
            db.HeroSnapshots.Add(new HeroSnapshot(0, 1, 1, "S1", 5,
                DateTime.UtcNow, new CharacterSheet { Race = "Dwarf", Classes = [new ClassLevel { Class = "Cleric", Level = 5 }] }));
            await db.SaveChangesAsync();
        }

        await using (var db = pg.NewContext())
        {
            var snap = await db.HeroSnapshots.SingleAsync();
            snap.Sheet.Level.Should().Be(5);
            snap.Sheet.Race.Should().Be("Dwarf");
            snap.Sheet.Class.Should().Be("Cleric");
        }
    }
}