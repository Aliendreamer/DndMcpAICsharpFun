using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;

public sealed class EntityHitCollapserTests
{
    private static readonly Dictionary<string, BookType> Books = new() { ["PHB"] = BookType.Core, ["XGE"] = BookType.Supplement };

    private static EntitySearchHit Hit(string id, string src, float score, string edition = "Edition2014") =>
        new(TestEnvelopes.Make(id, "Fireball", EntityType.Spell, edition, sourceBook: src), score, "pt-" + id);

    [Fact]
    public void Duplicates_collapse_to_winner_with_max_score()
    {
        var core = Hit("a", "PHB", score: 0.4f);
        var supp = Hit("b", "XGE", score: 0.9f);
        var result = EntityHitCollapser.Collapse([core, supp], Books);

        result.Should().HaveCount(1);
        result[0].Envelope.Id.Should().Be("a");     // Core wins
        result[0].Score.Should().Be(0.9f);          // max score of the group
    }

    [Fact]
    public void Distinct_editions_both_survive()
    {
        var y2014 = Hit("a", "PHB", 0.5f, "Edition2014");
        var y2024 = Hit("b", "PHB", 0.6f, "Edition2024");
        var result = EntityHitCollapser.Collapse([y2014, y2024], Books);
        result.Should().HaveCount(2);
    }

    [Fact]
    public void Non_duplicate_hits_pass_through()
    {
        var fireball = Hit("a", "PHB", 0.5f);
        var iceknife = new EntitySearchHit(
            TestEnvelopes.Make("c", "Ice Knife", EntityType.Spell, "Edition2014", sourceBook: "PHB"), 0.3f, "pt-c");
        EntityHitCollapser.Collapse([fireball, iceknife], Books).Should().HaveCount(2);
    }
}