namespace DndMcpAICsharpFun.Tools.ModelEval;

/// <summary>
/// Cross-call scratch state for the stub tools. Scenarios run strictly sequentially (one scenario,
/// N sequential runs), so plain statics are safe — no concurrency. The runner calls <see cref="Reset"/>
/// before each run and sets <see cref="AskRulesReturnsEmpty"/> from the scenario's StubEmpty flag.
/// </summary>
internal static class StubState
{
    public static readonly List<string> InvokedTools = [];
    public static bool AskRulesReturnsEmpty;

    public static void Reset()
    {
        InvokedTools.Clear();
        AskRulesReturnsEmpty = false;
    }
}
