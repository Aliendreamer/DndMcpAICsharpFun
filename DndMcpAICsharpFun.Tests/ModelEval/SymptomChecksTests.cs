using FluentAssertions;

using DndMcpAICsharpFun.Tools.ModelEval;

namespace DndMcpAICsharpFun.Tests.ModelEval;

public class SymptomChecksTests
{
    [Fact]
    public void NoList_ProseText_Adheres()
    {
        SymptomChecks.NoList("It costs 750 gp and takes 30 workweeks.").Should().BeTrue();
    }

    [Fact]
    public void NoList_TwoNumberedLines_Fails()
    {
        SymptomChecks.NoList("Here you go:\n1. Materials: 750 gp\n2. Time: 30 workweeks").Should().BeFalse();
    }

    [Fact]
    public void NoList_SingleBulletMarker_Tolerated()
    {
        SymptomChecks.NoList("- 750 gp materials").Should().BeTrue();
    }

    [Fact]
    public void NoList_TwoBulletMarkers_Fails()
    {
        SymptomChecks.NoList("- 750 gp\n- 30 workweeks").Should().BeFalse();
    }

    [Fact]
    public void NoList_EmptyString_Adheres()
    {
        SymptomChecks.NoList("").Should().BeTrue();
    }

    [Fact]
    public void NoList_WhitespaceOnly_Adheres()
    {
        SymptomChecks.NoList("   ").Should().BeTrue();
    }

    [Fact]
    public void NumberLabel_CorrectLabelNearValue_Adheres()
    {
        SymptomChecks.NumberLabel("The materials cost 750 gp.", "750", "cost", ["market value"]).Should().BeTrue();
    }

    [Fact]
    public void NumberLabel_WrongLabelNearValue_NoCorrectLabel_Fails()
    {
        SymptomChecks.NumberLabel("The market value is 750 gp.", "750", "cost", ["market value"]).Should().BeFalse();
    }

    [Fact]
    public void NumberLabel_WrongLabelNearDifferentNumber_CorrectLabelNearValue_Adheres()
    {
        SymptomChecks.NumberLabel(
            "It costs 750 gp, well under the 1500 gp market value.", "750", "cost", ["market value"])
            .Should().BeTrue();
    }

    [Fact]
    public void NumberLabel_ValueEmbeddedInLargerNumber_DoesNotMatch()
    {
        SymptomChecks.NumberLabel("The materials are 7500 gp.", "750", "cost", ["market value"]).Should().BeTrue();
    }

    [Fact]
    public void NumberLabel_ValueAbsent_Adheres()
    {
        SymptomChecks.NumberLabel("Nothing relevant here.", "750", "cost", ["market value"]).Should().BeTrue();
    }
}
