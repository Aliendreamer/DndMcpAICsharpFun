using DndMcpAICsharpFun.Features.Resolution;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.CharacterAdvice;

public class SlotTableReaderTests
{
    [Fact]
    public void FullCaster_level3_hasTwoFirstAndTwoSecond()
    {
        var slots = MulticlassSlotTableSeeder.SlotsForCasterLevel(new("multiclass", 3));
        slots.Should().Equal(4, 2, 0, 0, 0, 0, 0, 0, 0);
    }

    [Fact]
    public void HalfCaster_level1_hasNoSlots()
    {
        MulticlassSlotTableSeeder.SlotsForCasterLevel(new("half", 1))
            .Should().OnlyContain(x => x == 0);
    }

    [Fact]
    public void None_orOutOfRange_isAllZero()
    {
        MulticlassSlotTableSeeder.SlotsForCasterLevel(new("none", 0)).Should().OnlyContain(x => x == 0);
        MulticlassSlotTableSeeder.SlotsForCasterLevel(new("multiclass", 25)).Should().OnlyContain(x => x == 0);
    }
}
