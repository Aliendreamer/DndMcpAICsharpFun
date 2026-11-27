namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

internal enum RunOutcome { Pass, WrongTool, BindError, WrongToolData, WrongAnswer, InfraError }

/// <param name="Reason">"not-offered"/"not-chosen" for WrongTool, or the error text.</param>
internal sealed record RunScore(RunOutcome Outcome, string? Reason, long LatencyMs, RunDetail? Detail = null);

/// <summary>What the model actually did in one run: tools it called and (truncated) reply text.</summary>
/// <param name="AppSearched">The model called no tool; the chat searched for it and asked again.</param>
/// <param name="Tools">Each call's arguments and what it returned, so a miss can be traced to its search.</param>
internal sealed record RunDetail(
    IReadOnlyList<string> CalledTools, string Text, bool AppSearched = false, IReadOnlyList<ToolTrace>? Tools = null)
{
    public const int MaxTextLength = 600;

    public static RunDetail From(TurnDto t) => new(
        t.ToolCalls.Select(tc => tc.Name).ToList(),
        t.Text.Length > MaxTextLength ? t.Text[..MaxTextLength] : t.Text,
        t.ToolCalls.Any(tc => tc.ByApp),
        t.ToolCalls.Select(ToolTrace.From).ToList());

    public static RunDetail Infra(string error) => new(
        [], error.Length > MaxTextLength ? error[..MaxTextLength] : error);
}

/// <param name="ResultLength">The whole result's length; Result keeps its first <see cref="MaxResultLength"/> characters.</param>
internal sealed record ToolTrace(string Name, string Arguments, string? Result, int ResultLength, string? Error, bool ByApp)
{
    public const int MaxResultLength = 8000;

    public static ToolTrace From(ToolCallDto tc) => new(
        tc.Name, tc.ArgumentsJson,
        tc.Result is { Length: > MaxResultLength } r ? r[..MaxResultLength] : tc.Result,
        tc.Result?.Length ?? 0, tc.Error, tc.ByApp);
}

internal static class Attribution
{
    public static RunScore Score(EvalCase c, TurnDto t) => ScoreCore(c, t) with { Detail = RunDetail.From(t) };

    private static RunScore ScoreCore(EvalCase c, TurnDto t)
    {
        var called = t.ToolCalls.Where(tc => c.ExpectedTools.Contains(tc.Name)).ToList();
        if (called.Count == 0)
        {
            var offered = c.ExpectedTools.Any(e => t.OfferedTools.Contains(e));
            return new RunScore(RunOutcome.WrongTool, offered ? "not-chosen" : "not-offered", t.LatencyMs);
        }

        if (called.Any(tc => tc.Error is not null) && !called.Any(tc => tc.Error is null && tc.Result is not null))
        {
            var err = called.First(tc => tc.Error is not null).Error;
            return new RunScore(RunOutcome.BindError, err, t.LatencyMs);
        }

        if (c.ToolDataFacts is not null)
        {
            var data = string.Join("\n", called.Select(tc => tc.Result ?? string.Empty));
            if (!FactMatcher.AllMatch(c.ToolDataFacts, data))
            {
                var notFound = data.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    || data.Contains("No entities found", StringComparison.OrdinalIgnoreCase);
                return new RunScore(RunOutcome.WrongToolData, notFound ? "not-found" : null, t.LatencyMs);
            }
        }

        if (!FactMatcher.AllMatch(c.AnswerFacts, t.Text))
            return new RunScore(RunOutcome.WrongAnswer, null, t.LatencyMs);

        return new RunScore(RunOutcome.Pass, null, t.LatencyMs);
    }
}