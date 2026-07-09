using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

public class FivetoolsSourceRegistryTests
{
    [Fact]
    public void Registry_contains_class_and_subclass_entries_for_fighter()
    {
        var entries = FivetoolsSourceRegistry.AllEntries;
        entries.Should().Contain(e =>
            e.EntityType == EntityType.Class &&
            e.RelativePath.Contains("class-fighter") &&
            e.JsonArrayKey == "class");
        entries.Should().Contain(e =>
            e.EntityType == EntityType.Subclass &&
            e.RelativePath.Contains("class-fighter") &&
            e.JsonArrayKey == "subclass");
    }

    [Fact]
    public void Registry_contains_spell_bestiary_and_global_entries()
    {
        var entries = FivetoolsSourceRegistry.AllEntries;
        entries.Should().Contain(e => e.EntityType == EntityType.Spell);
        entries.Should().Contain(e => e.EntityType == EntityType.Monster);
        entries.Should().Contain(e => e.EntityType == EntityType.Race && e.JsonArrayKey == "race");
        entries.Should().Contain(e => e.EntityType == EntityType.God && e.JsonArrayKey == "deity");
        entries.Should().Contain(e => e.EntityType == EntityType.Rule && e.JsonArrayKey == "variantrule");
    }
}