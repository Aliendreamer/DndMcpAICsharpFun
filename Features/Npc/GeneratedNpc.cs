namespace DndMcpAICsharpFun.Features.Npc;

/// <summary>A grounded NPC stat block resolved from a real corpus Monster entity.</summary>
public sealed record NpcStatBlock(
    string Name, string SourceBook, double? Cr, int? Hp,
    int? Str, int? Dex, int? Con, int? Int, int? Wis, int? Cha, string CanonicalText);

/// <summary>The generation result: the grounded stat block when the archetype resolved, or the
/// not-in-corpus flag + the available-archetypes menu for the caller to re-pick.</summary>
public sealed record GeneratedNpc(
    string Concept, string Archetype, NpcStatBlock? StatBlock,
    bool ArchetypeInCorpus, IReadOnlyList<string> AvailableArchetypes);
