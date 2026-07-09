using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Tests;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.FivetoolsIngestion;

public sealed class MonsterNameCleanupTests
{
    private static readonly EntityNameMatcher Matcher =
        new(new EntityNameIndex(TestPaths.RepoFile("5etools")));

    private const string BookKey = "mm14";
    private const string Backfill = "5etools-backfill";

    // Minimal Monster envelope; grounded unless dataSource == Backfill.
    private static EntityEnvelope Monster(string name, string dataSource = "extraction", bool needsReview = false) =>
        new(
            Id: EntityIdSlug.For(BookKey, EntityType.Monster, name),
            Type: EntityType.Monster,
            Name: name,
            SourceBook: "MM",
            Edition: "Edition2014",
            Page: null,
            FirstAppearedIn: new FirstAppearance("MM", "Edition2014", null),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: JsonDocument.Parse("{\"hp\":367}").RootElement.Clone(),
            DataSource: dataSource,
            NeedsReview: needsReview);

    private static EntityEnvelope NonMonster(string name) =>
        Monster(name) with { Type = EntityType.Spell, Fields = JsonDocument.Parse("{}").RootElement.Clone() };

    [Fact]
    public void Garbled_dragon_name_is_rewritten_and_id_recomputed()
    {
        var garbled = Monster("ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil");
        var (entities, counts) = MonsterNameCleanup.Clean(new[] { garbled }, Matcher, BookKey);

        counts.Cleaned.Should().Be(1);
        var e = entities.Single();
        e.Name.Should().Be("Ancient Black Dragon");
        e.Id.Should().Be(EntityIdSlug.For(BookKey, EntityType.Monster, "Ancient Black Dragon"));
        e.DataSource.Should().Be("extraction");            // preserved
        e.Fields.GetProperty("hp").GetInt32().Should().Be(367); // preserved
    }

    [Fact]
    public void Clean_names_and_nonmonsters_untouched_and_idempotent()
    {
        var input = new[] { Monster("Dragon Turtle"), NonMonster("Fireball") };
        var (once, c1) = MonsterNameCleanup.Clean(input, Matcher, BookKey);

        c1.Should().Be(new MonsterNameCleanupCounts(0, 0, 0, Grounded: 1, Backfilled: 0));
        once.Select(e => (e.Name, e.Id)).Should().Equal(input.Select(e => (e.Name, e.Id)));

        var (twice, c2) = MonsterNameCleanup.Clean(once, Matcher, BookKey);
        c2.Cleaned.Should().Be(0);
        c2.Deduped.Should().Be(0);
        twice.Select(e => (e.Name, e.Id)).Should().Equal(once.Select(e => (e.Name, e.Id)));
    }

    [Fact]
    public void Cleaned_grounded_dragon_drops_its_backfill_duplicate()
    {
        var garbled = Monster("ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil");   // grounded
        var backfillDupe = Monster("Ancient Black Dragon", dataSource: Backfill);        // clean backfill
        var (entities, counts) = MonsterNameCleanup.Clean(new[] { garbled, backfillDupe }, Matcher, BookKey);

        counts.Cleaned.Should().Be(1);
        counts.Deduped.Should().Be(1);
        entities.Should().ContainSingle(e => e.Name == "Ancient Black Dragon");
        entities.Single(e => e.Name == "Ancient Black Dragon").DataSource.Should().Be("extraction"); // grounded kept
        counts.Grounded.Should().Be(1);
        counts.Backfilled.Should().Be(0);
    }

    [Fact]
    public void Grounded_vs_grounded_collision_keeps_first_and_flags_other()
    {
        var a = Monster("Ancient Black Dragon");                                          // already clean grounded
        var b = Monster("ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil");          // cleans to same name
        var (entities, counts) = MonsterNameCleanup.Clean(new[] { a, b }, Matcher, BookKey);

        counts.GroundedCollisionsFlagged.Should().Be(1);
        entities.Should().HaveCount(2);                                    // neither deleted
        entities.Select(e => e.Id).Should().OnlyHaveUniqueItems();         // no duplicate ids
        entities.Count(e => e.NeedsReview).Should().Be(1);                 // the second flagged
        entities.First(e => e.Name == "Ancient Black Dragon").NeedsReview.Should().BeFalse(); // first untouched

        var winner = entities.Single(e => !e.NeedsReview);
        winner.Name.Should().Be("Ancient Black Dragon");
        winner.Id.Should().Be(EntityIdSlug.For(BookKey, EntityType.Monster, "Ancient Black Dragon"));
    }

    [Theory]
    [InlineData(false)] // clean name first, garbled second
    [InlineData(true)]  // garbled name first, clean second
    public void Grounded_vs_grounded_collision_keeps_distinct_ids_regardless_of_source_order(bool garbledFirst)
    {
        var clean = Monster("Ancient Black Dragon");
        var garbled = Monster("ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil");
        var input = garbledFirst ? new[] { garbled, clean } : new[] { clean, garbled };

        var (entities, counts) = MonsterNameCleanup.Clean(input, Matcher, BookKey);

        entities.Should().HaveCount(2);
        entities.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        entities.Count(e => e.Id == EntityIdSlug.For(BookKey, EntityType.Monster, "Ancient Black Dragon"))
            .Should().Be(1);
        entities.Count(e => e.NeedsReview).Should().Be(1);
        counts.GroundedCollisionsFlagged.Should().Be(1);
    }

    [Fact]
    public void Output_has_no_duplicate_ids()
    {
        var garbledGrounded = Monster("ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil");
        var cleanGrounded = Monster("Ancient Black Dragon");
        var otherMonster = Monster("Dragon Turtle");
        var nonMonster = NonMonster("Fireball");

        var (entities, _) = MonsterNameCleanup.Clean(
            new[] { garbledGrounded, cleanGrounded, otherMonster, nonMonster }, Matcher, BookKey);

        entities.Select(e => e.Id).Should().OnlyHaveUniqueItems();
    }
}