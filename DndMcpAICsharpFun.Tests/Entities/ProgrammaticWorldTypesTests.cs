using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

/// <summary>Programmatic (schema "2") God, Rule, Plane, Faction, Location and Lore entities must load and render real text.</summary>
public class ProgrammaticWorldTypesTests
{
    private static readonly string Path_ =
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "programmatic", "world-types-sample.json");

    // id, type, name, a field key that must be present, a value the rendered text must contain
    public static TheoryData<string, EntityType, string, string, string> Cases => new()
    {
        { "phb14.god.anubis", EntityType.God, "Anubis", "domains", "Death" },
        { "phb14.rule.customizing-ability-scores", EntityType.Rule, "Customizing Ability Scores", "ruleType", "rule." },
        { "phb14.plane.the-material-plane", EntityType.Plane, "The Material Plane", "entries", "nexus" },
        { "ggr.faction.azorius-senate", EntityType.Faction, "Azorius Senate", "entries", "New Prahu" },
        { "erlw.location.the-treaty-of-thronehold", EntityType.Location, "The Treaty of Thronehold", "setting", "in Eberron" },
        { "erlw.lore.rising-from-the-last-war", EntityType.Lore, "Rising from the Last War", "settingContext", "Explore the lands" },
    };

    [Fact]
    public async Task Fixture_LoadsAllSixTypes_AsProgrammatic()
    {
        var file = await new CanonicalJsonLoader().LoadAsync(Path_, CancellationToken.None);
        file.SchemaVersion.Should().Be("2");
        file.Entities.Select(e => e.Type).Should().BeEquivalentTo(
            new[] { EntityType.God, EntityType.Rule, EntityType.Plane, EntityType.Faction, EntityType.Location, EntityType.Lore });
        file.Entities.Should().OnlyContain(e => e.DataSource == DataSources.Programmatic);
        file.Entities.Should().OnlyContain(e => e.Provenance != null && e.Checks.Count > 0);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Entity_KeepsIdentityAndFields_AndRendersRealText(
        string id, EntityType type, string name, string fieldKey, string expected)
    {
        var file = await new CanonicalJsonLoader().LoadAsync(Path_, CancellationToken.None);
        var e = file.Entities.Single(x => x.Id == id);

        e.Type.Should().Be(type);
        e.Name.Should().Be(name);
        e.Fields.TryGetProperty(fieldKey, out _).Should().BeTrue(fieldKey);

        var text = new EntityCanonicalTextDispatcher().Render(e);
        text.Should().StartWith($"{name}.");
        text.Should().NotStartWith($"{name} ({type})", "the typed renderer, not the degraded fallback, must run");
        text.Should().Contain(expected);
    }
}
