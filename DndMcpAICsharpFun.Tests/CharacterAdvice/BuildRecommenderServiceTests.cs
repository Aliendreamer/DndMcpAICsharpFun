using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.CharacterAdvice;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.CharacterAdvice;

/// <summary>
/// Configurable fake <see cref="IEntityRetrievalService"/>: each test wires <see cref="DiagnosticResponder"/>
/// and/or <see cref="SearchResponder"/> to canned per-<see cref="EntityType"/> results, and the fake
/// records the last <see cref="EntitySearchQuery"/> issued per type in <see cref="LastQueryByType"/> so
/// tests can assert what the service under test actually asked for (e.g. that a concept string reached
/// the Type=Spell/Feat query text) rather than merely that a menu came back non-empty.
/// </summary>
file sealed class FakeEntityRetrievalService : IEntityRetrievalService
{
    public readonly Dictionary<EntityType, EntitySearchQuery> LastQueryByType = [];
    public Func<EntitySearchQuery, IList<EntityDiagnosticResult>> DiagnosticResponder = _ => [];
    public Func<EntitySearchQuery, IList<EntitySearchResult>> SearchResponder = _ => [];

    public Task<EntityFullResult?> GetByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult<EntityFullResult?>(null);

    public Task<IList<EntitySearchResult>> SearchAsync(EntitySearchQuery query, CancellationToken ct)
    {
        if (query.Type is { } t) LastQueryByType[t] = query;
        return Task.FromResult(SearchResponder(query));
    }

    public Task<IList<EntityDiagnosticResult>> SearchDiagnosticAsync(EntitySearchQuery query, CancellationToken ct)
    {
        if (query.Type is { } t) LastQueryByType[t] = query;
        return Task.FromResult(DiagnosticResponder(query));
    }
}

/// <summary>
/// Unit coverage for <see cref="BuildRecommenderService"/>: the edition-pinned class lookup (mirrors
/// <see cref="LevelUpAdviceService"/>'s exact-name-and-edition match) and the cited build-option menus
/// it assembles from <see cref="EntityOptionProvider"/>. Entity retrieval is a hand-rolled fake — no
/// Qdrant/Postgres dependency, since the service under test touches neither directly.
/// </summary>
public sealed class BuildRecommenderServiceTests
{
    private static EntityDiagnosticResult MakeClassResult(string name, string edition, string fieldsJson)
    {
        using var doc = JsonDocument.Parse(fieldsJson);
        var slug = name.ToLowerInvariant();
        return new EntityDiagnosticResult(
            Id: $"phb14.class.{slug}",
            Type: EntityType.Class,
            Name: name,
            SourceBook: "PHB",
            Edition: edition,
            Page: 1,
            SettingTags: [],
            PointId: $"point-{slug}",
            Fields: doc.RootElement.Clone(),
            Score: 1.0f);
    }

    private static EntityDiagnosticResult MakeSubclassResult(string name, string className, string edition)
    {
        using var doc = JsonDocument.Parse($$"""{"className":"{{className}}"}""");
        var slug = name.ToLowerInvariant();
        return new EntityDiagnosticResult(
            Id: $"phb14.subclass.{slug}",
            Type: EntityType.Subclass,
            Name: name,
            SourceBook: "PHB",
            Edition: edition,
            Page: 1,
            SettingTags: [],
            PointId: $"point-{slug}",
            Fields: doc.RootElement.Clone(),
            Score: 1.0f);
    }

    private static EntitySearchResult MakeSearchResult(EntityType type, string name, string edition) =>
        new(
            Id: $"phb14.{type.ToString().ToLowerInvariant()}.{name.ToLowerInvariant()}",
            Type: type,
            Name: name,
            SourceBook: "PHB",
            Edition: edition,
            Page: 1,
            SettingTags: [],
            Snippet: $"{name} snippet",
            Score: 1.0f);

    [Fact]
    public async Task ValidClass_ReturnsStructuredInfoAndCitedMenus()
    {
        var fake = new FakeEntityRetrievalService
        {
            DiagnosticResponder = query => query.Type switch
            {
                EntityType.Class =>
                [
                    MakeClassResult(
                        "Fighter", "Edition2014",
                        """{"hd":{"number":1,"faces":10},"proficiency":["str","con"],"subclassTitle":"Martial Archetype"}""")
                ],
                EntityType.Subclass => [MakeSubclassResult("Champion", "Fighter", "Edition2014")],
                _ => []
            },
            SearchResponder = query => query.Type switch
            {
                EntityType.Feat =>
                [
                    MakeSearchResult(EntityType.Feat, "Tough", "Edition2014"),
                    MakeSearchResult(EntityType.Feat, "Alert", "Edition2014")
                ],
                _ => []
            }
        };
        var options = new EntityOptionProvider(fake);
        var sut = new BuildRecommenderService(fake, options);

        var rec = await sut.RecommendBuildOptionsAsync("Fighter", "a tanky front-liner", null, default);

        rec.ClassInCorpus.Should().BeTrue();
        rec.HitDie.Should().Be("d10");
        rec.SubclassTitle.Should().Be("Martial Archetype");
        rec.SaveProficiencies.Should().Contain("str");
        rec.SpellcastingAbility.Should().BeNull();
        rec.Subclasses.Should().NotBeEmpty();
        rec.Feats.Should().NotBeEmpty();
        rec.Spells.Should().BeEmpty(); // non-caster: spell retrieval is skipped entirely
    }

    [Fact]
    public async Task ClassNotInCorpus_ReturnsNotFoundWithAvailableClasses()
    {
        var fake = new FakeEntityRetrievalService
        {
            DiagnosticResponder = query => query.Type switch
            {
                // The "Artificer" query never matches by name — only Fighter/Wizard come back,
                // simulating a corpus that has other classes but not the requested one.
                EntityType.Class =>
                [
                    MakeClassResult("Fighter", "Edition2014", """{"hd":{"number":1,"faces":10}}"""),
                    MakeClassResult("Wizard", "Edition2014", """{"hd":{"number":1,"faces":6}}""")
                ],
                _ => []
            }
        };
        var options = new EntityOptionProvider(fake);
        var sut = new BuildRecommenderService(fake, options);

        var rec = await sut.RecommendBuildOptionsAsync("Artificer", "a gadgeteer", null, default);

        rec.ClassInCorpus.Should().BeFalse();
        rec.AvailableClasses.Should().Contain("Fighter");
        rec.Subclasses.Should().BeEmpty();
        rec.Feats.Should().BeEmpty();
        rec.Spells.Should().BeEmpty();
    }

    [Fact]
    public async Task CasterClass_ConceptReachesSpellRetrieval()
    {
        const string concept = "a battlefield controller who locks down enemies";
        var fake = new FakeEntityRetrievalService
        {
            DiagnosticResponder = query => query.Type switch
            {
                EntityType.Class =>
                [
                    MakeClassResult(
                        "Wizard", "Edition2014",
                        """{"hd":{"number":1,"faces":6},"proficiency":["int","wis"],"subclassTitle":"Arcane Tradition","spellcastingAbility":"int"}""")
                ],
                EntityType.Subclass => [MakeSubclassResult("Evocation", "Wizard", "Edition2014")],
                _ => []
            },
            SearchResponder = query => query.Type switch
            {
                EntityType.Feat => [MakeSearchResult(EntityType.Feat, "War Caster", "Edition2014")],
                EntityType.Spell => [MakeSearchResult(EntityType.Spell, "Web", "Edition2014")],
                _ => []
            }
        };
        var options = new EntityOptionProvider(fake);
        var sut = new BuildRecommenderService(fake, options);

        var rec = await sut.RecommendBuildOptionsAsync("Wizard", concept, null, default);

        rec.ClassInCorpus.Should().BeTrue();
        rec.SpellcastingAbility.Should().Be("int");
        rec.Spells.Should().NotBeEmpty();
        fake.LastQueryByType[EntityType.Spell].QueryText.Should().Be(concept);
        fake.LastQueryByType[EntityType.Feat].QueryText.Should().Be(concept);
    }


    [Theory]
    [InlineData(5, new[] { 1, 2, 3 })]
    [InlineData(null, new[] { 1 })]
    public async Task CasterClass_TargetLevelBoundsReachableSpellLevels(int? targetLevel, int[] expectedLevels)
    {
        const string concept = "a battlefield controller who locks down enemies";
        var spellLevelsQueried = new HashSet<int>();
        var fake = new FakeEntityRetrievalService
        {
            DiagnosticResponder = query => query.Type switch
            {
                EntityType.Class =>
                [
                    MakeClassResult(
                        "Wizard", "Edition2014",
                        """{"hd":{"number":1,"faces":6},"proficiency":["int","wis"],"subclassTitle":"Arcane Tradition","spellcastingAbility":"int"}""")
                ],
                EntityType.Subclass => [MakeSubclassResult("Evocation", "Wizard", "Edition2014")],
                _ => []
            },
            SearchResponder = query =>
            {
                if (query.Type == EntityType.Spell && query.SpellLevel is { } level)
                {
                    spellLevelsQueried.Add(level);
                    return [MakeSearchResult(EntityType.Spell, $"Spell{level}", "Edition2014")];
                }
                return query.Type switch
                {
                    EntityType.Feat => [MakeSearchResult(EntityType.Feat, "War Caster", "Edition2014")],
                    _ => []
                };
            }
        };
        var options = new EntityOptionProvider(fake);
        var sut = new BuildRecommenderService(fake, options);

        var rec = await sut.RecommendBuildOptionsAsync("Wizard", concept, targetLevel, default);

        rec.ClassInCorpus.Should().BeTrue();
        spellLevelsQueried.Should().BeEquivalentTo(expectedLevels);
        rec.Spells.Should().HaveCount(expectedLevels.Length);
        rec.Spells.Select(s => s.Id).Should().OnlyHaveUniqueItems();
        fake.LastQueryByType[EntityType.Spell].QueryText.Should().Be(concept);
        fake.LastQueryByType[EntityType.Feat].QueryText.Should().Be(concept);
    }
}
