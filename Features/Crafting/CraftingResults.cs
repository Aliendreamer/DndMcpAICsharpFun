namespace DndMcpAICsharpFun.Features.Crafting;

/// <summary>Deterministic nonmagical crafting result (XGE downtime formula).</summary>
public record NonmagicalCraft(int MaterialsGp, double TotalWorkweeks, double PerCrafterWorkweeks, int Days);

/// <summary>Deterministic magic-item crafting result (XGE rarity table).</summary>
public record MagicItemCraft(Rarity Rarity, int Workweeks, int GoldCostGp);
