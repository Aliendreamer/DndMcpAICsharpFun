using FluentAssertions;

using DndMcpAICsharpFun.Tools.ModelEval.Full;

namespace DndMcpAICsharpFun.Tests.ModelEval;

public class BaselineComparerTests
{
    private static CaseReport C(string id, string cat, bool pass) =>
        new(id, cat, "generated", pass ? 3 : 0, 3, pass, new[] { pass ? "pass" : "wrong-answer" });

    private static EvalReport R(params CaseReport[] cases) =>
        EvalReportTests.Report("m", DateTime.UtcNow, cases);

    private static CaseReport[] Spells(int passing, int total) =>
        Enumerable.Range(0, total).Select(i => C($"s{i}", "spell", i < passing)).ToArray();

    [Fact]
    public void NoBaseline_ExitZero_HasBaselineFalse()
    {
        var c = BaselineComparer.Compare(null, R(C("a", "spell", true)));
        c.HasBaseline.Should().BeFalse();
        BaselineComparer.ExitCode(c).Should().Be(0);
    }

    [Fact]
    public void TwoCaseDrop_Regressed_Exit2()
    {
        var c = BaselineComparer.Compare(R(Spells(5, 5)), R(Spells(3, 5)));
        c.RegressedCategories.Should().Contain("spell");
        c.ByCategory["spell"].Should().Be((5, 3));
        BaselineComparer.ExitCode(c).Should().Be(2);
    }

    [Fact]
    public void OneCaseDrop_NotRegressed_Exit0()
    {
        var c = BaselineComparer.Compare(R(Spells(5, 5)), R(Spells(4, 5)));
        c.RegressedCategories.Should().BeEmpty();
        BaselineComparer.ExitCode(c).Should().Be(0);
    }

    [Fact]
    public void NewlyFailingAndPassingListed()
    {
        var b = R(C("a", "spell", true), C("b", "spell", false));
        var cur = R(C("a", "spell", false), C("b", "spell", true));
        var c = BaselineComparer.Compare(b, cur);
        c.NewlyFailing.Should().Equal("a");
        c.NewlyPassing.Should().Equal("b");
    }

    [Fact]
    public void DifferentCaseSets_CompareSharedIdsOnly()
    {
        var b = R(C("a", "spell", true), C("b", "spell", true), C("c", "spell", true));
        var cur = R(C("b", "spell", true), C("c", "spell", false), C("d", "spell", true));
        var c = BaselineComparer.Compare(b, cur);
        c.SameCaseSet.Should().BeFalse();
        c.ByCategory["spell"].Should().Be((2, 1));
        c.NewlyFailing.Should().Equal("c");
        c.NewlyPassing.Should().BeEmpty();
    }
}
