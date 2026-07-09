using DndMcpAICsharpFun.Features.Entities;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class EntityReferenceResolverTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical", "test-book.json");

    [Fact]
    public async Task Empty_subclasses_yields_no_warnings()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);
        var resolver = new EntityReferenceResolver();
        var warnings = resolver.Resolve(file.Entities).ToList();
        warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Dangling_subclass_reference_emits_warning()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);
        // Mutate fixture in-memory: add subclass ref that doesn't exist.
        var modifiedFighter = file.Entities.Single(e => e.Id == "test-book.class.fighter") with
        {
            Fields = System.Text.Json.JsonSerializer.SerializeToElement(new
            {
                hitDie = "d10",
                primaryAbilities = new[] { "Strength" },
                savingThrowProficiencies = new[] { "Strength" },
                armorProficiencies = new[] { "heavy" },
                weaponProficiencies = new[] { "martial" },
                toolProficiencies = Array.Empty<string>(),
                skillChoices = new { count = 2, options = new[] { "Athletics" } },
                startingEquipment = Array.Empty<object>(),
                multiclass = new { prerequisites = new { @operator = "or", abilities = new Dictionary<string, int> { ["Strength"] = 13 } }, proficienciesGained = Array.Empty<string>() },
                spellcasting = (object?)null,
                subclassSelectionLevel = 3,
                subclasses = new[] { "test-book.subclass.does-not-exist" },
                asiLevels = Array.Empty<int>(),
                featuresByLevel = Array.Empty<object>(),
            })
        };
        var entities = file.Entities.Where(e => e.Id != "test-book.class.fighter").Append(modifiedFighter).ToList();

        var resolver = new EntityReferenceResolver();
        var warnings = resolver.Resolve(entities).ToList();

        warnings.Should().ContainSingle(w =>
            w.SourceEntityId == "test-book.class.fighter" &&
            w.MissingTargetId == "test-book.subclass.does-not-exist");
    }
}