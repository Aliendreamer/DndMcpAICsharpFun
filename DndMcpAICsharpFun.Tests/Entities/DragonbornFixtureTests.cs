using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Tests;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class DragonbornFixtureTests
{
    private static readonly string FixturePath =
        TestPaths.RepoFile("books/canonical/dragonborn-slice.json");

    [Fact]
    public async Task Dragonborn_fixture_loads_and_ancestry_table_red_row_is_correct()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);

        var ancestryTable = file.Tables.Single(t => t.Id == "phb14.table.draconic-ancestry");

        // Red is row index 7 (zero-based): Black=0, Blue=1, Brass=2, Bronze=3, Copper=4, Gold=5, Green=6, Red=7
        var redRow = ancestryTable.Rows[7];
        // columns: ancestry(0), damageType(1), breathArea(2), saveAbility(3)
        redRow.Cells[0].Value.Should().Be("Red");
        redRow.Cells[1].Value.Should().Be("fire");
        redRow.Cells[2].Value.Should().Be("15 ft. cone");
        redRow.Cells[3].Value.Should().Be("Dexterity");
    }

    [Fact]
    public async Task Dragonborn_fixture_tier_table_maps_tier1_to_1d10_and_tier3_to_3d6()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);

        var tierTable = file.Tables.Single(t => t.Id == "phb14.table.breath-damage-by-tier");

        // columns: tier(0), dice(1)
        var tier1Row = tierTable.Rows.Single(r => r.Cells[0].Value == "1");
        tier1Row.Cells[1].Value.Should().Be("1d10");

        var tier3Row = tierTable.Rows.Single(r => r.Cells[0].Value == "3");
        tier3Row.Cells[1].Value.Should().Be("3d6");
    }

    [Fact]
    public async Task Dragonborn_fixture_choiceset_has_10_options_all_pointing_to_ancestry_table()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);

        var choiceSet = file.ChoiceSets.Single(cs => cs.Id == "phb14.choiceset.draconic-ancestry");

        choiceSet.Options.Should().HaveCount(10);
        choiceSet.Options.Should().OnlyContain(o => o.TableId == "phb14.table.draconic-ancestry");
    }

    [Fact]
    public async Task Dragonborn_fixture_sample_cell_provenance_has_PHB_source_book()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);

        var ancestryTable = file.Tables.Single(t => t.Id == "phb14.table.draconic-ancestry");
        var sampleCell = ancestryTable.Rows[7].Cells[1]; // Red row, damageType cell

        sampleCell.Provenance.Should().NotBeNull();
        sampleCell.Provenance!.SourceBook.Should().Be("PHB");
        sampleCell.Provenance.Page.Should().Be(34);
    }
}