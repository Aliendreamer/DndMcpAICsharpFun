using System.Text.Json;

using DndMcpAICsharpFun.CompanionUI.Components;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Campaign;

/// <summary>
/// FormatEntry must never throw for malformed log payloads (JSON that deserializes but has
/// null required fields) — malformed entries are skipped, not crashed on. No database needed.
/// </summary>
public sealed class CampaignLogFormattingTests
{
    private static CampaignLogEntry Entry(CampaignLogKind kind, string payloadJson, string? label = "Label") => new()
    {
        Id = 1,
        CampaignId = 1,
        UserId = 1,
        Kind = kind,
        Label = label,
        Hidden = false,
        CreatedAt = DateTime.UtcNow,
        PayloadJson = payloadJson,
    };

    [Fact]
    public void Encounter_entry_with_null_Monsters_is_skipped_not_thrown()
    {
        // "Monsters" explicitly null — deserializes fine, but the old code did
        // payload.Monsters.Select(...) which threw NullReferenceException.
        var json = """{"Difficulty":"Deadly","TotalXp":500,"AdjustedXp":750,"PartyLevels":[3,3],"Monsters":null,"FullyMatched":true,"Note":null}""";
        var entry = Entry(CampaignLogKind.Encounter, json);

        var act = () => CampaignLog.FormatEntry(entry);

        act.Should().NotThrow();
        act().Should().BeNull();
    }

    [Fact]
    public void Encounter_entry_with_missing_monsters_field_is_skipped_not_thrown()
    {
        var json = """{"Difficulty":"Deadly","TotalXp":500,"AdjustedXp":750,"PartyLevels":[3,3],"FullyMatched":true}""";
        var entry = Entry(CampaignLogKind.Encounter, json);

        var act = () => CampaignLog.FormatEntry(entry);

        act.Should().NotThrow();
        act().Should().BeNull();
    }

    [Fact]
    public void Encounter_entry_with_null_entry_in_monsters_array_renders_placeholder_not_thrown()
    {
        var json = """{"Difficulty":"Deadly","TotalXp":500,"AdjustedXp":750,"PartyLevels":[3,3],"Monsters":[null],"FullyMatched":true}""";
        var entry = Entry(CampaignLogKind.Encounter, json);

        var act = () => CampaignLog.FormatEntry(entry);

        act.Should().NotThrow();
        act().Should().Be("Label · Deadly · ?");
    }

    [Fact]
    public void Well_formed_encounter_entry_renders_normally()
    {
        var payload = new EncounterLogPayload(
            "Deadly", 500, 750, new[] { 3, 3, 3, 3 },
            new[] { new EncounterMonsterLog("mm.monster.goblin", "Goblin", 0.25, 50) },
            true, null);
        var entry = Entry(CampaignLogKind.Encounter, JsonSerializer.Serialize(payload));

        CampaignLog.FormatEntry(entry).Should().Be("Label · Deadly · Goblin");
    }

    [Fact]
    public void Roll_entry_with_null_breakdown_renders_placeholder_not_thrown()
    {
        var json = """{"Expression":"2d6+3","Breakdown":null,"Total":12,"Dice":[4,5],"Kept":[4,5],"Mode":"Normal"}""";
        var entry = Entry(CampaignLogKind.Roll, json);

        var act = () => CampaignLog.FormatEntry(entry);

        act.Should().NotThrow();
        act().Should().Be("Label ·  · 12");
    }

    [Fact]
    public void Well_formed_roll_entry_renders_normally()
    {
        var payload = new RollLogPayload("2d6+3", "2d6+3 → [4,5]+3 = 12", 12, new[] { 4, 5 }, new[] { 4, 5 }, "Normal");
        var entry = Entry(CampaignLogKind.Roll, JsonSerializer.Serialize(payload));

        CampaignLog.FormatEntry(entry).Should().Be("Label · 2d6+3 → [4,5]+3 = 12 · 12");
    }

    [Fact]
    public void Garbage_json_is_skipped_not_thrown()
    {
        var entry = Entry(CampaignLogKind.Encounter, "{not valid json");

        var act = () => CampaignLog.FormatEntry(entry);

        act.Should().NotThrow();
        act().Should().BeNull();
    }
}
