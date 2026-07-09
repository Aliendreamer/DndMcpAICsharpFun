namespace DndMcpAICsharpFun.Domain.Entities;

public enum SpellcastingType { Full, Half, Third, Pact, Innate, None }

public enum SpellPreparation { Spellbook, Known, Prepared }

public sealed record SpellSlotsRow(int Level, IReadOnlyList<int> SlotsByLevel);

public sealed record PactSlotRow(int Level, int Slots, int SlotLevel);

public sealed record SpellcastingBlock(
    SpellcastingType Type,
    string? Ability,
    bool RitualCasting = false,
    SpellPreparation? Preparation = null,
    IReadOnlyList<int>? CantripsKnownByLevel = null,
    IReadOnlyList<int>? SpellsKnownByLevel = null,
    string? SpellList = null,
    IReadOnlyList<SpellSlotsRow>? SpellSlotsByLevel = null,
    IReadOnlyList<PactSlotRow>? PactSlotsByLevel = null);