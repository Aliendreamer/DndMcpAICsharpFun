using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.Fivetools;

public class ProjectTablesConsoleTests
{
    [Fact]
    public async Task Official_book_gets_normalized_resolution_artifacts_and_round_trips()
    {
        var (dir, fiveDir, canon) = Fixtures.OfficialBook("PHB");
        try
        {
            var res = await ProjectTablesRunner.RunOneAsync(canon, fiveDir, new CanonicalJsonLoader(), new CanonicalJsonWriter(), default);
            res.Skipped.Should().BeFalse();

            var reloaded = await new CanonicalJsonLoader().LoadAsync(canon, default);
            var ids = reloaded.Tables.Select(t => t.Id).ToList();
            ids.Should().Contain("phb14.table.draconic-ancestry").And.Contain("phb14.table.breath-damage-by-tier").And.OnlyHaveUniqueItems();
            ids.Should().Contain("phb14.table.life-domain-spells");

            // Resolution owns draconic-ancestry: it carries the NORMALIZED columns, not the generic 5etools shape.
            var draconic = reloaded.Tables.Single(t => t.Id == "phb14.table.draconic-ancestry");
            draconic.Columns.Should().Equal("ancestry", "damageType", "breathArea", "saveAbility");

            reloaded.ChoiceSets.Select(c => c.Id).Should().Contain("phb14.choiceset.draconic-ancestry");
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


    [Fact]
    public async Task Official_book_with_no_projectable_tables_is_skipped_and_untouched()
    {
        // A monster/reference book whose 5etools content yields 0 tables must NOT have its
        // existing tables wiped to empty — the projection only replaces when it has tables to offer.
        var (dir, fiveDir, canon) = Fixtures.OfficialBookEmptyData("MM");
        try
        {
            var before = await File.ReadAllTextAsync(canon);
            var res = await ProjectTablesRunner.RunOneAsync(canon, fiveDir, new CanonicalJsonLoader(), new CanonicalJsonWriter(), default);
            res.Skipped.Should().BeTrue();
            (await File.ReadAllTextAsync(canon)).Should().Be(before);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Book_with_only_subclass_spells_keeps_existing_tables_and_adds_spells()
    {
        var (dir, fiveDir, canon) = Fixtures.OfficialBookOnlySubclassSpells();
        try
        {
            var res = await ProjectTablesRunner.RunOneAsync(canon, fiveDir, new CanonicalJsonLoader(), new CanonicalJsonWriter(), default);
            res.Skipped.Should().BeFalse();
            var reloaded = await new CanonicalJsonLoader().LoadAsync(canon, default);
            var ids = reloaded.Tables.Select(t => t.Id).ToList();
            ids.Should().Contain("scag.table.existing-minerU", "existing tables are preserved, not wiped");
            ids.Should().Contain("scag.table.storm-sorcery-spells", "subclass-spells are added");
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
              {"type":"table","caption":"Draconic Ancestry","colLabels":["Dragon","Damage Type","Breath Weapon"],
               "rows":[["Black","Acid","5 by 30 ft. line (Dex. save)"]]}]}]}
            """);
            File.WriteAllText(Path.Combine(fiveDir, "class", "class-fighter.json"), """
            {"class":[{"name":"Fighter","source":"PHB","classFeatures":["Second Wind|Fighter||1"]}]}
            """);
            File.WriteAllText(Path.Combine(fiveDir, "class", "class-cleric.json"), """
            {"subclass":[{"name":"Life Domain","className":"Cleric","source":"PHB",
              "additionalSpells":[{"prepared":{"1":["bless","cure wounds"]}}]}]}
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


        public static (string Dir, string FiveDir, string CanonicalPath) OfficialBookEmptyData(string sourceKey)
        {
            var dir = Path.Combine(Path.GetTempPath(), "pt-" + Guid.NewGuid().ToString("N"));
            var fiveDir = Path.Combine(dir, "5etools");
            Directory.CreateDirectory(fiveDir); // no races/class/etc. -> projection yields 0 tables
            var canon = Path.Combine(dir, "canonical.json");
            File.WriteAllText(canon, $$"""
            {"schemaVersion":"1","book":{"sourceBook":"{{sourceKey}}","edition":"Edition2014","fileHash":"x","displayName":"Some Book"},"entities":[],"tables":[{"id":"{{EntityIdSlug.BookSlug(sourceKey)}}.table.existing","name":"Existing","columns":["a","b"],"rows":[]}]}
            """);
            return (dir, fiveDir, canon);
        }

        public static (string Dir, string FiveDir, string CanonicalPath) OfficialBookOnlySubclassSpells()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pt-" + Guid.NewGuid().ToString("N"));
            var fiveDir = Path.Combine(dir, "5etools");
            Directory.CreateDirectory(Path.Combine(fiveDir, "class"));
            File.WriteAllText(Path.Combine(fiveDir, "class", "class-sorcerer.json"), """
            {"subclass":[{"name":"Storm Sorcery","className":"Sorcerer","source":"SCAG",
              "additionalSpells":[{"known":{"1":["fog cloud"]}}]}]}
            """);
            var canon = Path.Combine(dir, "canonical.json");
            File.WriteAllText(canon, """
            {"schemaVersion":"1","book":{"sourceBook":"SCAG","edition":"Edition2014","fileHash":"x","displayName":"Sword Coast Adventurer's Guide"},
             "entities":[],"tables":[{"id":"scag.table.existing-minerU","name":"Existing","columns":["a","b"],"rows":[]}]}
            """);
            return (dir, fiveDir, canon);
        }
    }
}