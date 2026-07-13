namespace DndMcpAICsharpFun.Features.Rules;

/// <summary>
/// The core rulebooks rules-adjudication retrieval is scoped to. VALUES are the stable
/// `dnd_blocks.source_key` payloads (see <c>BookCatalog</c>), not display names — a mismatch scopes to
/// nothing. Scoping to these excludes monster/lore prose (e.g. Monster Manual) that a naive semantic
/// query surfaces for a rules question.
/// </summary>
public static class RuleSources
{
    public static readonly IReadOnlyCollection<string> Keys = ["PHB", "DMG"];

    /// <summary>Higher than the default so a multi-rule question (grapple + prone) can surface each rule.</summary>
    public const int TopK = 10;

    /// <summary>Per-topic result cap for multi-hop retrieval, so N topics don't balloon the passage count.</summary>
    public const int TopicTopK = 5;
}
