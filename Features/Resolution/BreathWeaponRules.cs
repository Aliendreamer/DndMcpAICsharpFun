namespace DndMcpAICsharpFun.Features.Resolution;

/// <summary>
/// Deterministic Dragonborn breath-weapon rules (PHB): damage scales by character level tier,
/// and the save DC is 8 + proficiency bonus + Constitution modifier.
/// </summary>
public static class BreathWeaponRules
{
    public static string DiceForLevel(int level) => level switch
    {
        <= 5 => "1d10",
        <= 10 => "2d6",
        <= 15 => "3d6",
        _ => "4d6",
    };

    public static int ProficiencyBonus(int level) => 2 + (level - 1) / 4;

    public static int SaveDc(int level, int conMod) => 8 + ProficiencyBonus(level) + conMod;
}