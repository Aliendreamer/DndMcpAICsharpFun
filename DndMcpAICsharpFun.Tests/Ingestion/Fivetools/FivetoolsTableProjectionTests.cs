using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.Fivetools;

public class FivetoolsTableProjectionTests
{
    private static string WriteFixture()
    {
        var dir = Path.Combine(Path.GetTempPath(), "5et-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "class"));
        File.WriteAllText(Path.Combine(dir, "races.json"), """
        {"race":[{"name":"Dragonborn","source":"PHB","page":34,"entries":[
          {"type":"table","caption":"Draconic Ancestry","colLabels":["Dragon","Damage Type"],"rows":[["Black","Acid"]]}]},
          {"name":"Elf","source":"XGE","entries":[{"type":"table","caption":"Ignore Me","colLabels":["x"],"rows":[["y"]]}]}]}
        """);
        File.WriteAllText(Path.Combine(dir, "class", "class-fighter.json"), """
        {"class":[{"name":"Fighter","source":"PHB","classFeatures":["Second Wind|Fighter||1"]}]}
        """);
        return dir;
    }

    [Fact]
    public void Builds_captioned_and_progression_for_the_source_key_only()
    {
        var dir = WriteFixture();
        try
        {
            var tables = new FivetoolsTableProjection().BuildForBook(dir, "PHB");
            var ids = tables.Select(t => t.Id).ToList();
            ids.Should().Contain("phb14.table.draconic-ancestry");
            ids.Should().Contain("phb14.table.fighter");
            ids.Should().NotContain("xge.table.ignore-me");   // wrong source key filtered out
            ids.Should().OnlyHaveUniqueItems();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
