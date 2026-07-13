namespace DndMcpAICsharpFun.Features.Rules;

/// <summary>
/// The core rulebooks rules-adjudication retrieval is scoped to. VALUES are the real
/// `dnd_blocks.source_book` display-name payloads (verified live 2026-07-13) — a mismatch scopes to
/// nothing. Scoping to these excludes monster/lore prose (e.g. Monster Manual) that a naive semantic
/// query surfaces for a rules question.
/// </summary>
public static class RuleSources
{
    public static readonly IReadOnlyCollection<string> Books =
        ["PlayerHandbook 2014", "Dungeon Master's Guide 2014"];

    /// <summary>Higher than the default so a multi-rule question (grapple + prone) can surface each rule.</summary>
    public const int TopK = 10;
}
