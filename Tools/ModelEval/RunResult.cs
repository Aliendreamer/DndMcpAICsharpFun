namespace DndMcpAICsharpFun.Tools.ModelEval;

internal sealed record RunResult(
    string? SelectedTool,
    bool BindOk,
    bool Adhered,
    long WallMs,
    long? FirstToolCallMs,
    bool? NoListOk = null,
    bool? NumberLabelOk = null);
