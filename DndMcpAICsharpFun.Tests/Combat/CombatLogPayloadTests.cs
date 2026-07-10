using System.Text.Json;
using DndMcpAICsharpFun.CompanionUI.Components;
using DndMcpAICsharpFun.Features.Campaigns;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

public sealed class CombatLogPayloadTests
{
    [Fact]
    public void Combat_payload_round_trips_through_json()
    {
        var payload = new CombatLogPayload(
            "Goblin Ambush",
            "Edition2014",
            3,
            new[] { new CombatCombatantLog("Aria", true, 17, 5, 12), new CombatCombatantLog("Goblin 1", false, 14, 0, 7) });

        var json = JsonSerializer.Serialize(payload);
        var back = JsonSerializer.Deserialize<CombatLogPayload>(json);

        back.Should().NotBeNull();
        back!.CombatName.Should().Be("Goblin Ambush");
        back.Rounds.Should().Be(3);
        back.Combatants.Should().HaveCount(2);
        back.Combatants[0].Name.Should().Be("Aria");
    }

    [Fact]
    public void FormatEntry_renders_a_combat_entry_with_name_round_and_count()
    {
        var payload = new CombatLogPayload("Goblin Ambush", "Edition2014", 3,
            new[] { new CombatCombatantLog("Aria", true, 17, 5, 12), new CombatCombatantLog("Goblin 1", false, 14, 0, 7) });
        var entry = new DndMcpAICsharpFun.Domain.CampaignLogEntry
        {
            Kind = DndMcpAICsharpFun.Domain.CampaignLogKind.Combat,
            Label = "Goblin Ambush",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload),
        };

        var line = CampaignLog.FormatEntry(entry);

        line.Should().NotBeNull();
        line.Should().Contain("Goblin Ambush").And.Contain("3 rounds").And.Contain("2 combatants");
    }
}
