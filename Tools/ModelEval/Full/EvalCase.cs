namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

/// <summary>A fact matches when ANY of its variants is contained in the (normalized) haystack.</summary>
internal sealed record Fact(IReadOnlyList<string> Variants);

/// <param name="Category">class, spell, crafting, or any string for manual cases.</param>
/// <param name="Tag">"generated" or "manual".</param>
/// <param name="ToolDataFacts">Facts expected in the tool results; null when not checked.</param>
internal sealed record EvalCase(
    string Id,
    string Category,
    string Tag,
    string Prompt,
    IReadOnlyList<string> ExpectedTools,
    IReadOnlyList<Fact>? ToolDataFacts,
    IReadOnlyList<Fact> AnswerFacts);
