using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

public class HeadingDespacerTests
{
    // ── Must-collapse corpus ─────────────────────────────────────────────────

    [Theory]
    [InlineData("ABER R ATIONS", "ABERRATIONS")]
    [InlineData("H U MANOIDS", "HUMANOIDS")]
    [InlineData("OPTIONAL C LASS FEATURES", "OPTIONAL CLASS FEATURES")]
    [InlineData("TH E WAR R IOR", "THE WARRIOR")]
    [InlineData("B EASTS", "BEASTS")]
    [InlineData("OOZ ES", "OOZES")]
    [InlineData("CE LESTIALS", "CELESTIALS")]
    [InlineData("CON STRUCTS", "CONSTRUCTS")]
    [InlineData("FI ENDS", "FIENDS")]
    [InlineData("UN  DEAD", "UNDEAD")]
    [InlineData("BARD C OLLEGE S", "BARD COLLEGES")]
    [InlineData("A CTI O NS", "ACTIONS")]
    public void Garbled_caps_headings_are_collapsed(string input, string expected)
        => HeadingDespacer.Normalize(input).Should().Be(expected);

    // ── Must-preserve corpus ─────────────────────────────────────────────────

    [Theory]
    [InlineData("PATH OF THE BEAST")]
    [InlineData("D4 ORIGIN")]
    [InlineData("Animating Performance")]
    [InlineData("BARD")]
    [InlineData("MONSTERS' DESIRES")]
    [InlineData("PARLEYING WITH MONSTERS")]
    [InlineData("1ST LEVEL")]
    [InlineData("OPTIONAL CLASS FEATURES")]
    public void Clean_headings_are_returned_unchanged(string input)
        => HeadingDespacer.Normalize(input).Should().Be(input);

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Empty_string_returns_empty()
        => HeadingDespacer.Normalize(string.Empty).Should().Be(string.Empty);

    [Fact]
    public void Single_fragment_returns_unchanged()
        => HeadingDespacer.Normalize("MONSTERS").Should().Be("MONSTERS");

    [Fact]
    public void Mixed_case_returns_unchanged()
        => HeadingDespacer.Normalize("Animating Performance").Should().Be("Animating Performance");
}
