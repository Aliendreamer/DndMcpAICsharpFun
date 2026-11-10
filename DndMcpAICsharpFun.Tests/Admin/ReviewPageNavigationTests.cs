using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin.Review;
using FluentAssertions;
using ReviewPage = DndMcpAICsharpFun.CompanionUI.Pages.Review.Review;

namespace DndMcpAICsharpFun.Tests.Admin;

public sealed class ReviewPageNavigationTests
{
    private static ReviewListItem Item(string id) => new(id, id, EntityType.Monster, "bgg", 1, true, []);

    [Fact]
    public void AfterApprove_InFlaggedList_TheNextItemTakesTheApprovedOnesPlace()
    {
        // approved "b" (was index 1) has left the flagged-only list
        var items = new[] { Item("a"), Item("c"), Item("d") };

        ReviewPage.NextAfterApprove(items, "b", 1).Should().Be("c");
    }

    [Fact]
    public void AfterApprove_WhenStillListed_StepsPastIt()
    {
        var items = new[] { Item("a"), Item("b"), Item("c") };

        ReviewPage.NextAfterApprove(items, "b", 1).Should().Be("c");
    }

    [Fact]
    public void AfterApprove_TheLastItem_SelectsNothingOrThePreviousEnd()
    {
        ReviewPage.NextAfterApprove([Item("a"), Item("b")], "c", 2).Should().BeNull();
        ReviewPage.NextAfterApprove([Item("a"), Item("b")], "b", 1).Should().BeNull();
        ReviewPage.NextAfterApprove([], "a", 0).Should().BeNull();
    }


    [Fact]
    public void TypeFilter_KeepsOnlyThatType_AndEmptyKeepsAll()
    {
        var lore = new ReviewListItem("l", "l", EntityType.Lore, "erlw", 1, true, []);
        var items = new[] { Item("a"), lore, Item("b") };

        ReviewPage.FilterByType(items, "Lore").Should().ContainSingle().Which.Id.Should().Be("l");
        ReviewPage.FilterByType(items, "Monster").Select(i => i.Id).Should().Equal("a", "b");
        ReviewPage.FilterByType(items, "").Should().HaveCount(3);
    }

    [Fact]
    public void AllButLore_HidesLore_AndIsTheDefaultFilter()
    {
        var lore = new ReviewListItem("l", "l", EntityType.Lore, "erlw", 1, true, []);
        var items = new[] { Item("a"), lore, Item("b") };

        ReviewPage.FilterByType(items, ReviewPage.AllButLore).Select(i => i.Id).Should().Equal("a", "b");
        ReviewPage.TypeLabel(ReviewPage.AllButLore).Should().Be("all but Lore");
        ReviewPage.TypeLabel("Monster").Should().Be("Monster");
    }


    [Fact]
    public void WhenAFilteredTypeIsDone_TheTypeWithMostLeftIsNext_OrAllWhenNoneLeft()
    {
        var counts = new Dictionary<string, int> { ["Monster"] = 0, ["Rule"] = 4, ["Lore"] = 9 };

        ReviewPage.NextType(counts, "Monster").Should().Be("Lore");
        ReviewPage.NextType(new Dictionary<string, int> { ["Monster"] = 0 }, "Monster").Should().Be("");
    }


    [Fact]
    public void MissingSchemaFields_ListsAllowedFieldsTheEntityLacks_ButNotConfidence()
    {
        var schema = System.Text.Json.JsonDocument.Parse(
            """{"properties":{"type":{},"value":{},"weight":{},"entries":{},"confidence":{}}}""").RootElement;
        var fields = System.Text.Json.JsonDocument.Parse("""{"type":"TAH","entries":[]}""").RootElement;
        var empty = System.Text.Json.JsonDocument.Parse("{}").RootElement;

        ReviewPage.MissingSchemaFields(fields, schema).Should().Equal("value", "weight");
        ReviewPage.MissingSchemaFields(empty, schema).Should().Equal("type", "value", "weight", "entries");
    }


    [Fact]
    public void FieldHint_DescribesTheShape_FromTheSchemaProperty()
    {
        static System.Text.Json.JsonElement P(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement;

        FieldHints.Describe(P("""{"type":["integer","null"],"format":"int32"}""")).Should().Be("integer");
        FieldHints.Describe(P("""{"type":["array","null"],"items":{"type":["string","object"]}}"""))
            .Should().Be("array of string | object");
        FieldHints.Describe(P("""{"type":"string","enum":["low","high"]}""")).Should().Be("string, one of: low, high");
        FieldHints.Describe(P("{}")).Should().Be("any JSON");
    }
}
