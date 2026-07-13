using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Npc;

namespace DndMcpAICsharpFun.Features.SessionPrep;

/// <summary>A cohesive, campaign-scoped prep packet: an encounter for the party, a fitting NPC, and
/// setting lore hooks — each a grounded sub-result the persona weaves into a session outline.</summary>
public sealed record SessionPrepPacket(
    string Theme, BuiltEncounter Encounter, GeneratedNpc Npc, SettingLoreResult LoreHooks);
