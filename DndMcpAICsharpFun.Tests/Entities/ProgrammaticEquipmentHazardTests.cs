using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

/// <summary>Phase 2b programmatic output (equipment and hazards) must load and render, not degrade to the name fallback.</summary>
public class ProgrammaticEquipmentHazardTests
{
    private static readonly string Path_ =
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "programmatic", "phase2b-equipment-hazards-sample.json");

    private static readonly EntityType[] Expected =
    {
        EntityType.Weapon, EntityType.Armor, EntityType.Item, EntityType.VehicleMount,
        EntityType.Object, EntityType.Trap, EntityType.DiseasePoison, EntityType.Condition,
    };

    [Fact]
    public async Task Fixture_LoadsAllEightTypes_WithRawFieldsKept()
    {
        var entities = (await new CanonicalJsonLoader().LoadAsync(Path_, CancellationToken.None)).Entities;
        entities.Select(e => e.Type).Should().BeEquivalentTo(Expected);
        foreach (var e in entities)
            e.Fields.EnumerateObject().Should().NotBeEmpty(e.Type.ToString());
    }

    [Fact]
    public async Task Dispatcher_RendersRealText_ForEachNewType()
    {
        var entities = (await new CanonicalJsonLoader().LoadAsync(Path_, CancellationToken.None)).Entities;
        var dispatcher = new EntityCanonicalTextDispatcher();
        foreach (var e in entities)
        {
            var text = dispatcher.Render(e);
            text.Should().NotBeNullOrWhiteSpace(e.Type.ToString());
            text.Should().NotStartWith($"{e.Name} ({e.Type})", e.Type.ToString());
        }
    }
}
