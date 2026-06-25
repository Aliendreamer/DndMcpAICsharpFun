namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Recognises a complete creature stat block by its unambiguous markers (Armor Class + Hit Points +
/// Challenge). Used as a DETERMINISTIC override in the orchestrator: a candidate carrying a full stat
/// block IS a Monster, so it is extracted with the Monster schema directly rather than offered the
/// content-first decline branch — the model otherwise sometimes declines a clear stat block
/// (the Aboleth case). "Challenge X (Y XP)" is creature-specific, so non-creature tables/items that
/// happen to mention AC/HP are not matched.
/// </summary>
public static class StatBlockSignature
{
    public static bool IsCompleteStatBlock(string text) =>
        !string.IsNullOrEmpty(text)
        && Has(text, "Armor Class")
        && Has(text, "Hit Points")
        && Has(text, "Challenge");

    private static bool Has(string text, string token) =>
        text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
