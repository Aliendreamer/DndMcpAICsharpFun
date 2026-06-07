using DndMcpAICsharpFun.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Persistence;

public sealed class AppDbContextSmokeTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task All_DbSets_are_queryable_on_a_fresh_migrated_database()
    {
        await using var db = _db.CreateDbContext();
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
        await using (var db = _db.CreateDbContext())
        {
            db.HeroSnapshots.Add(new HeroSnapshot(0, 1, 1, "S1", 5,
                DateTime.UtcNow, new CharacterSheet { Level = 5, Race = "Dwarf", Class = "Cleric" }));
            await db.SaveChangesAsync();
        }

        await using (var db = _db.CreateDbContext())
        {
            var snap = await db.HeroSnapshots.SingleAsync();
            snap.Sheet.Level.Should().Be(5);
            snap.Sheet.Race.Should().Be("Dwarf");
            snap.Sheet.Class.Should().Be("Cleric");
        }
    }
}
