using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.Fivetools;

public class SubclassSpellsProjectorTests
{
    private static string WriteFixture()
    {
        var dir = Path.Combine(Path.GetTempPath(), "5scs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "class"));
        File.WriteAllText(Path.Combine(dir, "class", "class-cleric.json"), """
        {"subclass":[{"name":"Life Domain","shortName":"Life","className":"Cleric","source":"PHB","page":60,
          "additionalSpells":[{"prepared":{"1":["bless","cure wounds"],"3":["lesser restoration","spiritual weapon"]}}]},
          {"name":"Order Domain","className":"Cleric","source":"PHB"}]}
        """);
        File.WriteAllText(Path.Combine(dir, "class", "class-warlock.json"), """
        {"subclass":[{"name":"The Fiend","className":"Warlock","source":"PHB",
          "additionalSpells":[{"expanded":{"s1":["burning hands","command"],"s3":["fireball","stinking cloud"]}}]}]}
        """);
        return dir;
    }

    [Fact]
    public void Projects_prepared_and_expanded_subclass_spells()
    {
        var dir = WriteFixture();
        try
        {
            var tables = SubclassSpellsProjector.Project(dir, "PHB");
            var ids = tables.Select(t => t.Id).ToList();
            ids.Should().Contain("phb14.table.life-domain-spells");
            ids.Should().Contain("phb14.table.the-fiend-spells");
            ids.Should().NotContain(i => i.Contains("order-domain")); // no additionalSpells → no table

            var life = tables.Single(t => t.Id == "phb14.table.life-domain-spells");
            life.Columns.Should().Equal("level", "spells");
            life.Rows[0].Cells[0].Value.Should().Be("1");
            life.Rows[0].Cells[1].Value.Should().Contain("bless").And.Contain("cure wounds");

            // expanded: s1 -> char level 1, s3 -> char level 5
            var fiend = tables.Single(t => t.Id == "phb14.table.the-fiend-spells");
            fiend.Rows.Select(r => r.Cells[0].Value).Should().Contain("1").And.Contain("5");
            fiend.Rows.Single(r => r.Cells[0].Value == "5").Cells[1].Value.Should().Contain("fireball");
        }
        finally { Directory.Delete(dir, true); }
    }
}
