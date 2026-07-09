using System.Text.Json;

using DndMcpAICsharpFun.Domain;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Domain;

public sealed class CharacterSheetResolvedChoicesTests
{
    [Fact]
    public void ResolvedChoices_round_trips_with_default_options()
    {
        var sheet = new CharacterSheet();
        sheet.ResolvedChoices["ancestry"] = "phb14.choiceset.draconic-ancestry:Red";
        var rt = JsonSerializer.Deserialize<CharacterSheet>(
            JsonSerializer.Serialize(sheet, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!;
        rt.ResolvedChoices.Should().ContainKey("ancestry")
            .WhoseValue.Should().Be("phb14.choiceset.draconic-ancestry:Red");
    }

    [Fact]
    public void Old_snapshot_without_ResolvedChoices_deserializes_to_empty()
    {
        const string old = "{\"Race\":\"Dragonborn\",\"Level\":3}";
        var s = JsonSerializer.Deserialize<CharacterSheet>(old, (JsonSerializerOptions?)null)!;
        s.ResolvedChoices.Should().NotBeNull().And.BeEmpty();
    }
}