using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.CharacterAdvice;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.CharacterAdvice;

/// <summary>The programmatic (Python extractor) PHB output must work with the coach's ref parser and the renderers.</summary>
public class ProgrammaticClassRefsTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "programmatic", name);

    private static async Task<IReadOnlyList<EntityEnvelope>> LoadAsync(string name) =>
        (await new CanonicalJsonLoader().LoadAsync(Fixture(name), CancellationToken.None)).Entities;

    [Fact]
    public async Task EveryClass_ParsesAsManyRefsAsClassFeaturesItems()
    {
        var loader = new CanonicalJsonLoader();
        var classes = (await LoadAsync("phb14-classes.json")).Where(e => e.Type == EntityType.Class).ToList();
        classes.Should().HaveCount(12);
        foreach (var c in classes)
        {
            var fields = loader.DeserialiseFields<ClassFields>(c);
            fields.ClassFeatures.Should().NotBeNullOrEmpty(c.Name);
            ClassFeatureRefParser.Parse(fields.ClassFeatures, "classFeature")
                .Should().HaveCount(fields.ClassFeatures!.Count, c.Name);
        }
    }

    [Fact]
    public async Task Barbarian_IncludesRageAtLevel1_WithEmptySource()
    {
        var loader = new CanonicalJsonLoader();
        var barb = (await LoadAsync("phb14-classes.json")).Single(e => e.Type == EntityType.Class && e.Name == "Barbarian");
        var refs = ClassFeatureRefParser.Parse(loader.DeserialiseFields<ClassFields>(barb).ClassFeatures, "classFeature");
        refs.Should().Contain(new FeatureRef("Rage", "", 1));
    }

    [Fact]
    public async Task EverySubclass_LosesNoFeatureRef()
    {
        var loader = new CanonicalJsonLoader();
        var subs = (await LoadAsync("phb14-classes.json")).Where(e => e.Type == EntityType.Subclass).ToList();
        subs.Should().NotBeEmpty();
        foreach (var s in subs)
        {
            var fields = loader.DeserialiseFields<SubclassFields>(s);
            if (fields.SubclassFeatures is null) continue;
            ClassFeatureRefParser.Parse(fields.SubclassFeatures, "subclassFeature")
                .Should().HaveCount(fields.SubclassFeatures.Count, s.Name);
        }
    }

    [Fact]
    public async Task Dispatcher_RendersNonEmptyText_ForAllSixTypes()
    {
        var dispatcher = new EntityCanonicalTextDispatcher();
        var all = (await LoadAsync("phb14-classes.json")).Where(e => e.Type is EntityType.Class or EntityType.Subclass)
            .GroupBy(e => e.Type).Select(g => g.First())
            .Concat(await LoadAsync("phb14-character-sample.json")).ToList();
        all.Select(e => e.Type).Should().BeEquivalentTo(new[]
        {
            EntityType.Class, EntityType.Subclass, EntityType.Race, EntityType.Subrace, EntityType.Feat, EntityType.Background,
        });
        foreach (var e in all)
        {
            var text = dispatcher.Render(e);
            text.Should().NotBeNullOrWhiteSpace(e.Type.ToString());
            // Guard against the name-only degraded fallback hiding a render failure.
            text.Should().NotStartWith($"{e.Name} ({e.Type})", e.Type.ToString());
        }
    }
}
