using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record SubclassFields(
    string ParentClass,
    SpellcastingBlock? Spellcasting,
    IReadOnlyList<ClassLevelEntry> FeaturesByLevel);
