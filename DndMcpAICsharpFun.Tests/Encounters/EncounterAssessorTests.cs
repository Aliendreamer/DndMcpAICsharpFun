using DndMcpAICsharpFun.Domain;              // DndVersion
using DndMcpAICsharpFun.Features.Encounters;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Encounters;

public sealed class EncounterAssessorTests
{
    private static readonly IReadOnlyList<int> Party4L5 = [5, 5, 5, 5];

    private static IReadOnlyList<MonsterRef> ThreeCr3Monsters() =>
    [
        new MonsterRef("mm.monster.one", "Monster One", 3, EncounterMath.CrToXp(3)),
        new MonsterRef("mm.monster.two", "Monster Two", 3, EncounterMath.CrToXp(3)),
        new MonsterRef("mm.monster.three", "Monster Three", 3, EncounterMath.CrToXp(3))
    ];

    [Fact]
    public void Assess_2014_applies_multiplier_and_reports_hard()
    {
        var assessor = new EncounterAssessor();

        var result = assessor.Assess(Party4L5, ThreeCr3Monsters(), DndVersion.Edition2014);

        result.Difficulty.Should().Be(Difficulty.Hard);
        result.TotalMonsterXp.Should().Be(2100);
        result.AdjustedXp.Should().Be(4200); // 2100 × 2.0 (3 monsters, party of 4)
        result.Boundaries.Should().ContainEquivalentOf(new BandBoundary(Difficulty.Deadly, 4400));
    }

    [Fact]
    public void Assess_2024_no_multiplier_and_reports_easy()
    {
        var assessor = new EncounterAssessor();

        var result = assessor.Assess(Party4L5, ThreeCr3Monsters(), DndVersion.Edition2024);

        result.Difficulty.Should().Be(Difficulty.Easy);
        result.TotalMonsterXp.Should().Be(2100);
        result.AdjustedXp.Should().Be(result.TotalMonsterXp);
    }
}