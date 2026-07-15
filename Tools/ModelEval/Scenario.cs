namespace DndMcpAICsharpFun.Tools.ModelEval;

/// <param name="ExpectedTool">Tool name the model SHOULD select, or null for the negative (no-tool) case.</param>
/// <param name="StubEmpty">When true, the runner sets StubState.AskRulesReturnsEmpty so ask_rules returns no passages (fabrication test).</param>
/// <param name="AdherenceCheck">Runs against the final assistant text; true = adhered.</param>
/// <param name="NoListCheck">Optional extra per-run tally for list-iness (true = adhered/prose); null = not applicable to this scenario.</param>
/// <param name="NumberLabelCheck">Optional extra per-run tally for number mis-labeling (true = adhered); null = not applicable to this scenario.</param>
internal sealed record Scenario(
    string Name,
    string Prompt,
    string? ExpectedTool,
    bool StubEmpty,
    Func<string, bool> AdherenceCheck,
    Func<string, bool>? NoListCheck = null,
    Func<string, bool>? NumberLabelCheck = null);
