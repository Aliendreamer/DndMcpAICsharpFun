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
}