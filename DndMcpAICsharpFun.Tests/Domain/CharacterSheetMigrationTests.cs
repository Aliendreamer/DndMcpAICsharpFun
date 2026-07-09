using System.Text.Json;

using DndMcpAICsharpFun.Domain;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Domain;

public sealed class CharacterSheetMigrationTests
{
    [Fact]
    public void Legacy_flat_snapshot_loads_as_one_class_character()
    {
        // A pre-multiclass row: flat Class/Level, no "Classes" array.
        const string legacy = """
        { "Race": "Human", "Class": "Wizard", "Subclass": "Evocation", "Level": 5,
          "Constitution": 14 }
        """;

        var sheet = JsonSerializer.Deserialize<CharacterSheet>(legacy)!;

        sheet.Classes.Should().ContainSingle();
        sheet.Class.Should().Be("Wizard");
        sheet.Subclass.Should().Be("Evocation");
        sheet.Level.Should().Be(5);
    }

    [Fact]
    public void Round_trip_of_a_migrated_sheet_is_stable_and_drops_legacy_keys()
    {
        const string legacy = """{ "Class": "Wizard", "Level": 5 }""";

        var first = JsonSerializer.Deserialize<CharacterSheet>(legacy)!;
        var reserialized = JsonSerializer.Serialize(first);
        var second = JsonSerializer.Deserialize<CharacterSheet>(reserialized)!;

        second.Class.Should().Be("Wizard");
        second.Level.Should().Be(5);
        second.Classes.Should().ContainSingle();
        // Legacy top-level "Class"/"Level" keys are not echoed back (only "Classes" is written).
        using var doc = JsonDocument.Parse(reserialized);
        doc.RootElement.TryGetProperty("Classes", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("Level", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("Class", out _).Should().BeFalse();
    }

    [Fact]
    public void Sheet_with_neither_classes_nor_flat_class_stays_empty()
    {
        const string bare = """{ "Race": "Elf" }""";
        var sheet = JsonSerializer.Deserialize<CharacterSheet>(bare)!;
        sheet.Classes.Should().BeEmpty();
        sheet.Class.Should().Be("");
        sheet.Level.Should().Be(0);
    }
}