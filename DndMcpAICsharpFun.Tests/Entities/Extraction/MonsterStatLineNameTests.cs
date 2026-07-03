using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class MonsterStatLineNameTests
{
    [Fact]
    public void Strip_ancient_black_dragon_statline_removes_suffix() =>
        MonsterStatLineName.Strip("ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil")
            .Should().Be("ANCIENT BLACK DRAGON");

    [Fact]
    public void Strip_animated_armor_statline_removes_suffix() =>
        MonsterStatLineName.Strip("ANIMATED ARMOR Medium construct, unaligned")
            .Should().Be("ANIMATED ARMOR");

    // Leading size word in the NAME itself ("GIANT") is preserved — only the trailing
    // "<Size> <type>" stat line ("Huge beast, unaligned") is stripped.
    [Fact]
    public void Strip_giant_ape_statline_preserves_leading_size_word_in_name() =>
        MonsterStatLineName.Strip("GIANT APE Huge beast, unaligned")
            .Should().Be("GIANT APE");

    [Fact]
    public void Strip_swarm_of_bats_statline_covers_swarm_type() =>
        MonsterStatLineName.Strip("SWARM OF BATS Medium swarm of Tiny beasts, unaligned")
            .Should().Be("SWARM OF BATS");

    [Theory]
    [InlineData("Dragon Turtle")]
    [InlineData("Giant Ape")]
    [InlineData("Beholder")]
    [InlineData("Aboleth")]
    [InlineData("Swarm of Bats")]
    public void Strip_clean_name_without_trailing_statline_returns_unchanged(string name) =>
        MonsterStatLineName.Strip(name).Should().Be(name);

    // Whole-string-is-statline guard: stripping would leave nothing, so return unchanged.
    [Fact]
    public void Strip_whole_string_is_statline_returns_unchanged() =>
        MonsterStatLineName.Strip("Gargantuan dragon").Should().Be("Gargantuan dragon");
}
