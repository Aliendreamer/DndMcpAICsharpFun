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
    [InlineData("FI ENDS", "FIENDS")]
    [InlineData("UN  DEAD", "UNDEAD")]
    [InlineData("BARD C OLLEGE S", "BARD COLLEGES")]
    public void Garbled_caps_headings_are_collapsed(string input, string expected)
        => HeadingDespacer.Normalize(input).Should().Be(expected);

    // ── Vocabulary-dependent garble — accepted as missed merges ──────────────
    // These require dictionary knowledge that the despacer intentionally lacks.
    // The downstream needsReview heuristic catches them. The key invariant is
    // idempotence: a second pass must produce the same output.

    [Theory]
    // "CON" is 3 letters (never garbage), so CON STRUCTS cannot be merged without
    // vocabulary knowledge. Preserved as-is.
    [InlineData("CON STRUCTS", "CON STRUCTS")]
    // "CTI" is 3 letters (never garbage). "O" and "NS" are 1–2 letter garbage so
    // O merges right into ONS, leaving "A CTI ONS". Partial normalisation is acceptable.
    [InlineData("A CTI O NS", "A CTI ONS")]
    public void Vocabulary_dependent_garble_is_partially_or_fully_preserved(string input, string expected)
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
    // 3-letter fragments must never merge — false merge guard cases
    [InlineData("WAR DOMAIN")]
    [InlineData("HEX BLADE")]
    [InlineData("RED DRAGON")]
    [InlineData("GOD OF WAR")]
    // D&D abbreviations must never merge
    [InlineData("XP COSTS")]
    [InlineData("AC BONUS")]
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

    // ── Idempotence ──────────────────────────────────────────────────────────
    // Normalize must be idempotent: a second pass must produce identical output.

    [Theory]
    [InlineData("ABER R ATIONS")]
    [InlineData("H U MANOIDS")]
    [InlineData("OPTIONAL C LASS FEATURES")]
    [InlineData("TH E WAR R IOR")]
    [InlineData("B EASTS")]
    [InlineData("OOZ ES")]
    [InlineData("CE LESTIALS")]
    [InlineData("FI ENDS")]
    [InlineData("UN  DEAD")]
    [InlineData("BARD C OLLEGE S")]
    [InlineData("CON STRUCTS")]
    [InlineData("A CTI O NS")]
    [InlineData("WAR DOMAIN")]
    [InlineData("HEX BLADE")]
    [InlineData("RED DRAGON")]
    [InlineData("GOD OF WAR")]
    [InlineData("XP COSTS")]
    [InlineData("AC BONUS")]
    [InlineData("PATH OF THE BEAST")]
    [InlineData("OPTIONAL CLASS FEATURES")]
    public void Normalize_is_idempotent(string input)
    {
        var once = HeadingDespacer.Normalize(input);
        var twice = HeadingDespacer.Normalize(once);
        twice.Should().Be(once);
    }
}