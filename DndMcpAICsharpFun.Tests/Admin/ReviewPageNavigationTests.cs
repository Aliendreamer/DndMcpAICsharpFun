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
}
