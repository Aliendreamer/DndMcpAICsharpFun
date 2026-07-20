using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.Fivetools;

public class DraconicAncestryResolutionProjectorTests
{
    private static string WriteFixture()
    {
        var dir = Path.Combine(Path.GetTempPath(), "5res-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "races.json"), """
        {"race":[{"name":"Dragonborn","source":"PHB","page":34,"entries":[
          {"type":"table","caption":"Draconic Ancestry","colLabels":["Dragon","Damage Type","Breath Weapon"],
           "rows":[["Black","Acid","5 by 30 ft. line (Dex. save)"],
                   ["Green","Poison","15 ft. cone (Con. save)"]]}]}]}
        """);
        return dir;
    }

    [Fact]
    public void Produces_normalized_table_choiceset_and_tier_for_phb()
    {
        var dir = WriteFixture();
        try
        {
            var a = DraconicAncestryResolutionProjector.Project(dir, "PHB");
            var draconic = a.Tables.Single(t => t.Id == "phb14.table.draconic-ancestry");
            draconic.Columns.Should().Equal("ancestry", "damageType", "breathArea", "saveAbility");
            draconic.Rows[0].Cells.Select(c => c.Value).Should().Equal("Black", "acid", "5 by 30 ft. line", "Dexterity");
            draconic.Rows[1].Cells.Select(c => c.Value).Should().Equal("Green", "poison", "15 ft. cone", "Constitution");

            var tier = a.Tables.Single(t => t.Id == "phb14.table.breath-damage-by-tier");
            tier.Columns.Should().Equal("tier", "dice");
            tier.Rows.Select(r => r.Cells[1].Value).Should().Equal("1d10", "2d6", "3d6", "4d6");

            var cs = a.ChoiceSets.Single(c => c.Id == "phb14.choiceset.draconic-ancestry");
            cs.Name.Should().Be("Draconic Ancestry");
            cs.Options[0].Key.Should().Be("Black");
            cs.Options[0].TableId.Should().Be("phb14.table.draconic-ancestry");
            cs.Options[0].RowIndex.Should().Be(0);
            cs.Options[1].Key.Should().Be("Green");
            cs.Options[1].RowIndex.Should().Be(1);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Returns_empty_for_non_phb_book()
    {
        var dir = WriteFixture();
        try
        {
            var a = DraconicAncestryResolutionProjector.Project(dir, "MM");
            a.Tables.Should().BeEmpty();
            a.ChoiceSets.Should().BeEmpty();
        }
        finally { Directory.Delete(dir, true); }
    }
}
