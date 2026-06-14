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
        string dataSource = "llm",
        string name = "Foo") =>
        new(
            Id: id, Type: type, Name: name, SourceBook: "TCE", Edition: "Edition2014",
            Page: page,
            FirstAppearedIn: new FirstAppearance("TCE", "Edition2014", page),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: canonicalText,
            Fields: JsonDocument.Parse(fields).RootElement.Clone(),
            DataSource: dataSource,
            Srd: srd, Srd52: srd52, BasicRules2024: basicRules2024,
            Keywords: keywords ?? Array.Empty<string>());

    // ── NEW deep-merge behavior ────────────────────────────────────────────────

    [Fact]
    public void Fivetools_scalar_wins_for_non_narrative_key()
    {
        // OCR noise "l/4" → clean 5etools "1/4"
        var canonical = MakeEnvelope(fields: """{"cr":"l/4"}""");
        var existing  = MakeEnvelope(fields: """{"cr":"1/4"}""", dataSource: "5etools");

        var merged = EntityMerger.Merge(canonical, existing);

        merged.Fields.GetProperty("cr").GetString().Should().Be("1/4");
    }

    [Fact]
    public void Canonical_narrative_entries_are_preserved()
    {
        // Both sides have "entries"; our LLM prose must win.
        var canonical = MakeEnvelope(fields: """{"entries":["Our lore prose."]}""");
        var existing  = MakeEnvelope(fields: """{"entries":["5etools terse text."]}""",
                                     dataSource: "5etools");

        var merged = EntityMerger.Merge(canonical, existing);

        var entries = merged.Fields.GetProperty("entries");
        entries[0].GetString().Should().Be("Our lore prose.");
    }

    [Fact]
    public void Fivetools_fills_missing_structured_field()
    {
        // Canonical lacks "components"; 5etools has it.
        var canonical = MakeEnvelope(fields: """{"level":3}""");
        var existing  = MakeEnvelope(fields: """{"level":3,"components":{"v":true,"s":true}}""",
                                     dataSource: "5etools");

        var merged = EntityMerger.Merge(canonical, existing);

        merged.Fields.TryGetProperty("components", out var comp).Should().BeTrue();
        comp.TryGetProperty("v", out var v).Should().BeTrue();
        v.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Canonical_key_kept_when_fivetools_lacks_it()
    {
        // Canonical has "customField"; 5etools doesn't → keep canonical value.
        var canonical = MakeEnvelope(fields: """{"level":3,"customField":"ours"}""");
        var existing  = MakeEnvelope(fields: """{"level":3}""", dataSource: "5etools");

        var merged = EntityMerger.Merge(canonical, existing);

        merged.Fields.GetProperty("customField").GetString().Should().Be("ours");
    }

    [Fact]
    public void Fivetools_name_wins_unless_manual()
    {
        var canonical = MakeEnvelope(name: "OCR Noisy Name", dataSource: "llm");
        var existing  = MakeEnvelope(name: "Clean Name", dataSource: "5etools");

        var merged = EntityMerger.Merge(canonical, existing);

        merged.Name.Should().Be("Clean Name");
    }

    [Fact]
    public void Manual_name_is_protected()
    {
        var canonical = MakeEnvelope(name: "Hand Corrected", dataSource: "manual");
        var existing  = MakeEnvelope(name: "5etools Name", dataSource: "5etools");

        var merged = EntityMerger.Merge(canonical, existing);

        merged.Name.Should().Be("Hand Corrected");
    }

    [Fact]
    public void DataSource_is_llm_after_merge_unless_manual()
    {
        var canonical = MakeEnvelope(dataSource: "llm");
        var existing  = MakeEnvelope(dataSource: "5etools");

        EntityMerger.Merge(canonical, existing).DataSource.Should().Be("llm");
    }

    [Fact]
    public void DataSource_stays_manual_when_canonical_is_manual()
    {
        var canonical = MakeEnvelope(dataSource: "manual");
        var existing  = MakeEnvelope(dataSource: "5etools");

        EntityMerger.Merge(canonical, existing).DataSource.Should().Be("manual");
    }

    // ── EXISTING tests (behaviour unchanged) ─────────────────────────────────

    [Fact]
    public void Fivetools_srd_flags_always_win()
    {
        var canonical = MakeEnvelope(srd: false, srd52: false, basicRules2024: false);
        var existing  = MakeEnvelope(srd: true,  srd52: true,  basicRules2024: true,
                                     dataSource: "5etools");
        var merged = EntityMerger.Merge(canonical, existing);
        merged.Srd.Should().BeTrue();
        merged.Srd52.Should().BeTrue();
        merged.BasicRules2024.Should().BeTrue();
    }

    [Fact]
    public void Fivetools_srd_flags_only_win_when_existing_is_5etools()
    {
        var canonical = MakeEnvelope(srd: false, srd52: false, basicRules2024: false);
        var existing  = MakeEnvelope(srd: true,  srd52: true,  basicRules2024: true,
                                     dataSource: "llm");
        var merged = EntityMerger.Merge(canonical, existing);
        merged.Srd.Should().BeFalse();
        merged.Srd52.Should().BeFalse();
        merged.BasicRules2024.Should().BeFalse();
    }

    [Fact]
    public void Fivetools_type_wins_when_canonical_type_is_Class()
    {
        var canonical = MakeEnvelope(type: EntityType.Class);
        var existing  = MakeEnvelope(type: EntityType.Subclass, dataSource: "5etools");
        EntityMerger.Merge(canonical, existing).Type.Should().Be(EntityType.Subclass);
    }

    [Fact]
    public void Canonical_type_wins_when_not_Class()
    {
        var canonical = MakeEnvelope(type: EntityType.Spell);
        var existing  = MakeEnvelope(type: EntityType.Subclass, dataSource: "5etools");
        EntityMerger.Merge(canonical, existing).Type.Should().Be(EntityType.Spell);
    }

    [Fact]
    public void Keywords_are_unioned()
    {
        // New rule: UNION (not longer-wins) for keywords
        var canonical = MakeEnvelope(keywords: ["a", "b"]);
        var existing  = MakeEnvelope(keywords: ["b", "c"], dataSource: "5etools");

        var merged = EntityMerger.Merge(canonical, existing);
        merged.Keywords.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public void Keywords_union_deduplicates()
    {
        var canonical = MakeEnvelope(keywords: ["fiend", "demon"]);
        var existing  = MakeEnvelope(keywords: ["demon", "shapechanger"], dataSource: "5etools");

        var merged = EntityMerger.Merge(canonical, existing);
        merged.Keywords.Should().BeEquivalentTo(["fiend", "demon", "shapechanger"]);
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
    public void CanonicalText_always_from_canonical()
    {
        var canonical = MakeEnvelope(canonicalText: "canonical text");
        var existing  = MakeEnvelope(canonicalText: "old", dataSource: "5etools");
        EntityMerger.Merge(canonical, existing).CanonicalText.Should().Be("canonical text");
    }

    [Fact]
    public void No_existing_returns_canonical_as_is()
    {
        // When there is no existing record, Merge should not be called;
        // but the merger must handle "existing == canonical" (same object) gracefully.
        var canonical = MakeEnvelope(fields: """{"level":3,"entries":["Our prose."]}""");
        // We test by passing canonical as both sides — result should equal canonical.
        var merged = EntityMerger.Merge(canonical, canonical);
        merged.Fields.GetProperty("level").GetInt32().Should().Be(3);
        merged.Fields.GetProperty("entries")[0].GetString().Should().Be("Our prose.");
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

    [Fact]
    public void Description_is_narrative_key_canonical_wins()
    {
        var canonical = MakeEnvelope(fields: """{"description":"Our description."}""");
        var existing  = MakeEnvelope(fields: """{"description":"5etools description."}""",
                                     dataSource: "5etools");

        EntityMerger.Merge(canonical, existing)
            .Fields.GetProperty("description").GetString()
            .Should().Be("Our description.");
    }

    [Fact]
    public void Text_is_narrative_key_canonical_wins()
    {
        var canonical = MakeEnvelope(fields: """{"text":"Our text."}""");
        var existing  = MakeEnvelope(fields: """{"text":"5etools text."}""",
                                     dataSource: "5etools");

        EntityMerger.Merge(canonical, existing)
            .Fields.GetProperty("text").GetString()
            .Should().Be("Our text.");
    }

    [Fact]
    public void Star_entries_suffix_is_narrative_key_canonical_wins()
    {
        // e.g. "classEntries", "subclassEntries" → narrative
        var canonical = MakeEnvelope(fields: """{"classEntries":["Canonical class prose."]}""");
        var existing  = MakeEnvelope(fields: """{"classEntries":["5etools class prose."]}""",
                                     dataSource: "5etools");

        EntityMerger.Merge(canonical, existing)
            .Fields.GetProperty("classEntries")[0].GetString()
            .Should().Be("Canonical class prose.");
    }
}
