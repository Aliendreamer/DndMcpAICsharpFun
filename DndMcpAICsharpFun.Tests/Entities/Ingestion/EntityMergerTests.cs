using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using FluentAssertions;
using System.Text.Json;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

public class EntityMergerTests
{
    private static EntityEnvelope MakeEnvelope(
        string id = "tce.class.foo",
        EntityType type = EntityType.Class,
        string fields = "{}",
        string canonicalText = "",
        bool srd = false,
        bool srd52 = false,
        bool basicRules2024 = false,
        IReadOnlyList<string>? keywords = null,
        int? page = null,
        string dataSource = "llm") =>
        new(
            Id: id, Type: type, Name: "Foo", SourceBook: "TCE", Edition: "Edition2014",
            Page: page,
            FirstAppearedIn: new FirstAppearance("TCE", "Edition2014", page),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: canonicalText,
            Fields: JsonDocument.Parse(fields).RootElement.Clone(),
            DataSource: dataSource,
            Srd: srd, Srd52: srd52, BasicRules2024: basicRules2024,
            Keywords: keywords ?? Array.Empty<string>());

    [Fact]
    public void Canonical_fields_always_win()
    {
        var canonical = MakeEnvelope(fields: """{"key":"canonical"}""", canonicalText: "canonical text");
        var existing  = MakeEnvelope(fields: """{"key":"5etools"}""",  canonicalText: "old");
        var merged = EntityMerger.Merge(canonical, existing);
        merged.Fields.GetProperty("key").GetString().Should().Be("canonical");
        merged.CanonicalText.Should().Be("canonical text");
    }

    [Fact]
    public void Fivetools_srd_flags_always_win()
    {
        var canonical = MakeEnvelope(srd: false, srd52: false, basicRules2024: false);
        var existing  = MakeEnvelope(srd: true,  srd52: true,  basicRules2024: true,  dataSource: "5etools");
        var merged = EntityMerger.Merge(canonical, existing);
        merged.Srd.Should().BeTrue();
        merged.Srd52.Should().BeTrue();
        merged.BasicRules2024.Should().BeTrue();
    }

    [Fact]
    public void Fivetools_type_wins_when_canonical_type_is_Class()
    {
        var canonical = MakeEnvelope(type: EntityType.Class);
        var existing  = MakeEnvelope(type: EntityType.Subclass, dataSource: "5etools");
        var merged = EntityMerger.Merge(canonical, existing);
        merged.Type.Should().Be(EntityType.Subclass);
    }

    [Fact]
    public void Canonical_type_wins_when_not_Class()
    {
        var canonical = MakeEnvelope(type: EntityType.Spell);
        var existing  = MakeEnvelope(type: EntityType.Subclass, dataSource: "5etools");
        var merged = EntityMerger.Merge(canonical, existing);
        merged.Type.Should().Be(EntityType.Spell);
    }

    [Fact]
    public void Keywords_longest_list_wins()
    {
        var canonical = MakeEnvelope(keywords: ["a", "b", "c"]);
        var existing  = MakeEnvelope(keywords: ["a", "b"],       dataSource: "5etools");
        EntityMerger.Merge(canonical, existing).Keywords.Should().HaveCount(3);

        var canonical2 = MakeEnvelope(keywords: ["a"]);
        var existing2  = MakeEnvelope(keywords: ["a", "b", "c"], dataSource: "5etools");
        EntityMerger.Merge(canonical2, existing2).Keywords.Should().HaveCount(3);

        // equal length: existing wins (>= semantics)
        var canonical3 = MakeEnvelope(keywords: ["a", "b"]);
        var existing3  = MakeEnvelope(keywords: ["x", "y"], dataSource: "5etools");
        EntityMerger.Merge(canonical3, existing3).Keywords.Should().Equal(["x", "y"]);
    }

    [Fact]
    public void Page_existing_wins_if_set()
    {
        var canonical = MakeEnvelope(page: 42);
        var existing  = MakeEnvelope(page: 10, dataSource: "5etools");
        EntityMerger.Merge(canonical, existing).Page.Should().Be(10);
    }

    [Fact]
    public void Page_canonical_wins_if_existing_has_no_page()
    {
        var canonical = MakeEnvelope(page: 42);
        var existing  = MakeEnvelope(page: null, dataSource: "5etools");
        EntityMerger.Merge(canonical, existing).Page.Should().Be(42);
    }

    [Fact]
    public void DataSource_inherits_from_canonical()
    {
        var canonical = MakeEnvelope(dataSource: "llm");
        var existing  = MakeEnvelope(dataSource: "5etools");
        EntityMerger.Merge(canonical, existing).DataSource.Should().Be("llm");
    }

    [Fact]
    public void Merge_does_not_mutate_inputs()
    {
        var canonical = MakeEnvelope(srd: false);
        var existing  = MakeEnvelope(srd: true, dataSource: "5etools");
        _ = EntityMerger.Merge(canonical, existing);
        canonical.Srd.Should().BeFalse();
        existing.Srd.Should().BeTrue();
    }
}
