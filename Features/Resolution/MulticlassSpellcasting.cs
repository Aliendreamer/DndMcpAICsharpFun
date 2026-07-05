using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Resolution;

public enum CasterType { None, Full, Half, Third, Pact }

/// <summary>
/// Deterministic multiclass spellcasting composition (PHB "Multiclassing" — Spellcasting).
/// Combined caster level = Σ full-caster levels + ⌊half-caster levels / 2⌋ (Artificer ⌈level/2⌉)
/// + ⌊third-caster levels / 3⌋. Warlock Pact Magic is EXCLUDED and reported separately.
/// </summary>
public static class MulticlassSpellcasting
{
    private static readonly HashSet<string> FullCasters =
        new(StringComparer.OrdinalIgnoreCase) { "Bard", "Cleric", "Druid", "Sorcerer", "Wizard" };

    private static readonly HashSet<string> HalfCasters =
        new(StringComparer.OrdinalIgnoreCase) { "Paladin", "Ranger" };

    // Third-caster ONLY at these Fighter/Rogue subclasses.
    private static readonly HashSet<string> ThirdCasterSubclasses =
        new(StringComparer.OrdinalIgnoreCase) { "Eldritch Knight", "Arcane Trickster" };

    private static readonly Dictionary<string, string> Abilities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Bard"] = "Charisma", ["Cleric"] = "Wisdom", ["Druid"] = "Wisdom",
            ["Sorcerer"] = "Charisma", ["Wizard"] = "Intelligence",
            ["Paladin"] = "Charisma", ["Ranger"] = "Wisdom",
            ["Warlock"] = "Charisma", ["Artificer"] = "Intelligence",
            ["Eldritch Knight"] = "Intelligence", ["Arcane Trickster"] = "Intelligence",
        };

    public static CasterType Classify(ClassLevel c)
    {
        if (string.Equals(c.Class, "Warlock", StringComparison.OrdinalIgnoreCase)) return CasterType.Pact;
        if (FullCasters.Contains(c.Class)) return CasterType.Full;
        if (HalfCasters.Contains(c.Class)) return CasterType.Half;
        if (string.Equals(c.Class, "Artificer", StringComparison.OrdinalIgnoreCase)) return CasterType.Half;
        if (ThirdCasterSubclasses.Contains(c.Subclass)) return CasterType.Third;
        return CasterType.None;
    }

    public static int CombinedCasterLevel(IEnumerable<ClassLevel> classes)
    {
        var full = 0; var half = 0; var artificer = 0; var third = 0;
        foreach (var c in classes)
        {
            switch (Classify(c))
            {
                case CasterType.Full: full += c.Level; break;
                case CasterType.Half:
                    if (string.Equals(c.Class, "Artificer", StringComparison.OrdinalIgnoreCase))
                        artificer += c.Level;   // rounded UP, per class
                    else
                        half += c.Level;        // Paladin/Ranger, rounded DOWN
                    break;
                case CasterType.Third: third += c.Level; break;
            }
        }
        return full + half / 2 + (artificer + 1) / 2 + third / 3;
    }

    public sealed record PactMagic(int SlotCount, int SlotLevel);

    public static PactMagic? WarlockPact(IEnumerable<ClassLevel> classes)
    {
        var warlock = classes.FirstOrDefault(c =>
            string.Equals(c.Class, "Warlock", StringComparison.OrdinalIgnoreCase));
        if (warlock is null || warlock.Level < 1) return null;
        var lvl = warlock.Level;
        var slotLevel = lvl switch { 1 => 1, 2 or 3 or 4 => 2, 5 or 6 => 3, 7 or 8 => 4, _ => 5 };
        var slotCount = lvl switch { 1 => 1, 2 => 2, <= 10 => 2, <= 16 => 3, _ => 4 };
        return new PactMagic(slotCount, slotLevel);
    }

    public static string? SpellcastingAbility(string @class) =>
        Abilities.GetValueOrDefault(@class);
}
