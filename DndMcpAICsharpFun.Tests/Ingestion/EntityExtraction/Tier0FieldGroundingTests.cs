using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class Tier0FieldGroundingTests
{
    // The real OCR-noisy Draconic Ancestry prose (items 531/532/538).
    private const string DraconicSource =
        "DRACONIC ANCESTRY Dragon Type Damage Brealh Weapon Black Acid 5 by 30 fI. line (Dex. save) " +
        "Red Fire 15 fI. cone (Dex. save) White Cold 15 fI. cone (Con. save)";

    [Fact]
    public void OcrNoisyCorrectValue_IsGrounded()
    {
        // "Breath" extracted from the OCR-garbled "Brealh" must still ground.
        Tier0FieldGrounding.IsTextGrounded("Breath Weapon", DraconicSource).Should().BeTrue();
    }

    [Fact]
    public void CorrectTableCell_IsGrounded()
    {
        Tier0FieldGrounding.IsTextGrounded("15 ft cone", DraconicSource).Should().BeTrue();
    }

    [Fact]
    public void FabricatedValueAbsentFromSource_IsNotGrounded()
    {
        // A value the source never supports must NOT ground (the fabrication signal).
        Tier0FieldGrounding.IsTextGrounded("Fireball", "You hurl a bubble of acid at a creature").Should().BeFalse();
    }

    [Fact]
    public void WrongDamageType_IsNotGrounded()
    {
        // Source mentions fire/cold, not poison -> a fabricated "Poison" must not ground.
        Tier0FieldGrounding.IsTextGrounded("Poison", "Red Fire 15 ft cone White Cold 15 ft cone").Should().BeFalse();
    }

    [Fact]
    public void EmptyOrInsignificantValue_IsNotGrounded()
    {
        Tier0FieldGrounding.IsTextGrounded("", DraconicSource).Should().BeFalse();
        Tier0FieldGrounding.IsTextGrounded("  ", DraconicSource).Should().BeFalse();
    }

    [Fact]
    public void MultiWordName_RequiresAllSignificantTokens()
    {
        Tier0FieldGrounding.IsTextGrounded("Acid Splash", "ACID SPLASH you hurl a bubble of acid").Should().BeTrue();
        Tier0FieldGrounding.IsTextGrounded("Acid Splash", "you hurl a bubble of acid").Should().BeFalse();
    }
}
