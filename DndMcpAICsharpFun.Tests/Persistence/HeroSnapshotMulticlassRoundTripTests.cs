using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Persistence;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Tests.Persistence;

[Collection("postgres")]
public sealed class HeroSnapshotMulticlassRoundTripTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Multiclass_sheet_persists_and_reloads_with_all_classes()
    {
        var sheet = new CharacterSheet
        {
            Race = "Half-Elf",
            Classes =
            [
                new ClassLevel { Class = "Paladin", Level = 6, Subclass = "Devotion" },
                new ClassLevel { Class = "Sorcerer", Level = 2, Subclass = "Draconic" },
            ],
            Constitution = 14,
        };

        long id;
        await using (var db = pg.NewContext())
        {
            db.HeroSnapshots.Add(new HeroSnapshot(0, 1, 1, "MulticlassRoundTrip", sheet.Level, DateTime.UtcNow, sheet));
            await db.SaveChangesAsync();
            id = await db.HeroSnapshots
                .Where(s => s.HeroId == 1 && s.SessionNumber == 1)
                .Select(s => s.Id)
                .FirstAsync();
        }

        HeroSnapshot reloaded;
        await using (var db = pg.NewContext())
        {
            reloaded = await db.HeroSnapshots.SingleAsync(s => s.Id == id);
        }

        reloaded.Sheet.Classes.Should().HaveCount(2);
        reloaded.Sheet.Level.Should().Be(8);
        reloaded.Sheet.Class.Should().Be("Paladin");
    }
}