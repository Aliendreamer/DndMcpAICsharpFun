namespace DndMcpAICsharpFun.Features.Downtime;

/// <summary>
/// The source books downtime/crafting retrieval is scoped to. VALUES are the stable
/// `dnd_blocks.source_key` payloads (see <c>BookCatalog</c>), not display names — a mismatch scopes to
/// nothing. XGE holds the detailed downtime/crafting rules; the DMG holds the basics. Scoping to these
/// excludes unrelated prose a naive semantic query surfaces for a downtime question.
/// </summary>
public static class DowntimeSources
{
    public static readonly IReadOnlyCollection<string> Keys = ["XGE", "DMG"];

    /// <summary>Higher than the default so an activity spanning cost + time surfaces each passage.</summary>
    public const int TopK = 10;
}
