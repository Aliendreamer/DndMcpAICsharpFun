using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class EntityEnvelopeTests
{
    private static EntityEnvelope MakeEnvelope(bool needsReview = false) =>
        new(
            Id: "tce.subclass.circle-of-spores",
            Type: EntityType.Subclass,
            Name: "Circle of Spores",
            SourceBook: "TCE",
            Edition: "Edition2014",
            Page: null,
            FirstAppearedIn: new FirstAppearance("TCE", "Edition2014"),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: JsonDocument.Parse("{}").RootElement,
            NeedsReview: needsReview);

    [Fact]
    public void NeedsReview_defaults_to_false()
    {
        var e = new EntityEnvelope(
            Id: "x", Type: EntityType.Class, Name: "N", SourceBook: "PHB",
            Edition: "Edition2014", Page: null,
            FirstAppearedIn: new FirstAppearance("PHB", "Edition2014"),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "", Fields: JsonDocument.Parse("{}").RootElement);
        e.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public void NeedsReview_can_be_set_true()
    {
        var e = MakeEnvelope(needsReview: true);
        e.NeedsReview.Should().BeTrue();
    }

    [Fact]
    public void With_expression_propagates_NeedsReview()
    {
        var original = MakeEnvelope(needsReview: true);
        var copy = original with { Name = "Other" };
        copy.NeedsReview.Should().BeTrue();
    }

    [Fact]
    public void Notes_are_optional_and_omitted_from_json_when_absent()
    {
        var json = JsonSerializer.Serialize(MakeEnvelope());
        json.Should().NotContain("Notes");
        var back = JsonSerializer.Deserialize<EntityEnvelope>(json)!;
        back.Notes.Should().BeNull();
    }

    [Fact]
    public void Notes_round_trip_through_json()
    {
        var e = MakeEnvelope() with { Notes = [new EntityNote("Diancastra", "Most giants know better.", 127)] };
        var back = JsonSerializer.Deserialize<EntityEnvelope>(JsonSerializer.Serialize(e))!;
        back.Notes.Should().ContainSingle().Which.Should().Be(new EntityNote("Diancastra", "Most giants know better.", 127));
    }

    [Fact]
    public void Embedding_text_appends_the_notes_under_a_Notes_line_and_leaves_canonical_text_alone()
    {
        var notes = new[] { new EntityNote("Diancastra", "Most giants know better.", 127), new EntityNote(null, "Unsigned.", 127) };
        var text = DndMcpAICsharpFun.Features.Entities.CanonicalText.EntityEmbeddingText.For("Death Giant\nbody", notes);
        text.Should().StartWith("Death Giant\nbody\nNotes: ");
        text.Should().Contain("Most giants know better.").And.Contain("Diancastra").And.Contain("Unsigned.");
        DndMcpAICsharpFun.Features.Entities.CanonicalText.EntityEmbeddingText.For("x", null).Should().Be("x");
        DndMcpAICsharpFun.Features.Entities.CanonicalText.EntityEmbeddingText.For("x", []).Should().Be("x");
    }
}