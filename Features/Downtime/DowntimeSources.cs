namespace DndMcpAICsharpFun.Features.Downtime;

/// <summary>
/// The source books downtime/crafting retrieval is scoped to. VALUES are the real
/// `dnd_blocks.source_book` display-name payloads — a mismatch scopes to nothing. XGE holds the
/// detailed downtime/crafting rules; the DMG holds the basics. Scoping to these excludes unrelated
/// prose a naive semantic query surfaces for a downtime question. (XGE value confirmed live at Task 4.2.)
/// </summary>
public static class DowntimeSources
{
    public static readonly IReadOnlyCollection<string> Books =
        ["Xanathar's Guide to Everything", "Dungeon Master's Guide 2014"];

    /// <summary>Higher than the default so an activity spanning cost + time surfaces each passage.</summary>
    public const int TopK = 10;
}
