namespace DndMcpAICsharpFun.Features.Npc;

/// <summary>Curated common NPC stat-block names (the MM NPC appendix roster) used as the suggestion
/// list when the caller's chosen archetype isn't in the corpus. The service can fetch ANY monster by
/// name; this is only the fallback menu.</summary>
public static class NpcArchetypes
{
    public static readonly IReadOnlyList<string> Common =
    [
        "Commoner", "Guard", "Acolyte", "Bandit", "Cultist", "Noble", "Spy", "Thug", "Scout",
        "Bandit Captain", "Priest", "Veteran", "Knight", "Mage", "Assassin", "Berserker",
        "Gladiator", "Cult Fanatic", "Archmage",
    ];
}
