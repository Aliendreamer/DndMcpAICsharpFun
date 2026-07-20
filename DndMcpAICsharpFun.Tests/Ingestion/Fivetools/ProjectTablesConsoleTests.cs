using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.Fivetools;

public class ProjectTablesConsoleTests
{
    [Fact]
    public async Task Official_book_replaces_tables_and_round_trips()
    {
        var (dir, fiveDir, canon) = Fixtures.OfficialBook("PHB");
        try
        {
            var res = await ProjectTablesRunner.RunOneAsync(canon, fiveDir, new CanonicalJsonLoader(), new CanonicalJsonWriter(), default);
            res.Skipped.Should().BeFalse();
            var reloaded = await new CanonicalJsonLoader().LoadAsync(canon, default);
            reloaded.Tables.Select(t => t.Id).Should().Contain("phb14.table.draconic-ancestry").And.OnlyHaveUniqueItems();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Homebrew_book_is_skipped_and_untouched()
    {
        var (dir, fiveDir, canon) = Fixtures.HomebrewBook();
        try
        {
            var before = await File.ReadAllTextAsync(canon);
            var res = await ProjectTablesRunner.RunOneAsync(canon, fiveDir, new CanonicalJsonLoader(), new CanonicalJsonWriter(), default);
            res.Skipped.Should().BeTrue();
            (await File.ReadAllTextAsync(canon)).Should().Be(before);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static class Fixtures
    {
        public static (string Dir, string FiveDir, string CanonicalPath) OfficialBook(string sourceKey)
        {
            var dir = Path.Combine(Path.GetTempPath(), "pt-" + Guid.NewGuid().ToString("N"));
            var fiveDir = Path.Combine(dir, "5etools");
            Directory.CreateDirectory(Path.Combine(fiveDir, "class"));
            File.WriteAllText(Path.Combine(fiveDir, "races.json"), """
            {"race":[{"name":"Dragonborn","source":"PHB","page":34,"entries":[
              {"type":"table","caption":"Draconic Ancestry","colLabels":["Dragon","Damage Type"],"rows":[["Black","Acid"]]}]}]}
            """);
            File.WriteAllText(Path.Combine(fiveDir, "class", "class-fighter.json"), """
            {"class":[{"name":"Fighter","source":"PHB","classFeatures":["Second Wind|Fighter||1"]}]}
            """);

            var canon = Path.Combine(dir, "canonical.json");
            File.WriteAllText(canon, """
            {"schemaVersion":"1","book":{"sourceBook":"PHB","edition":"Edition2014","fileHash":"x","displayName":"PlayerHandbook 2014"},"entities":[],"tables":[]}
            """);
            return (dir, fiveDir, canon);
        }

        public static (string Dir, string FiveDir, string CanonicalPath) HomebrewBook()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pt-" + Guid.NewGuid().ToString("N"));
            var fiveDir = Path.Combine(dir, "5etools");
            Directory.CreateDirectory(fiveDir);

            var canon = Path.Combine(dir, "canonical.json");
            File.WriteAllText(canon, """
            {"schemaVersion":"1","book":{"sourceBook":"","edition":"Edition2014","fileHash":"x","displayName":"Homebrew Book"},"entities":[],"tables":[]}
            """);
            return (dir, fiveDir, canon);
        }
    }
}
