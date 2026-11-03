namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

internal enum RunOutcome { Pass, WrongTool, BindError, WrongToolData, WrongAnswer, InfraError }

/// <param name="Reason">"not-offered"/"not-chosen" for WrongTool, or the error text.</param>
internal sealed record RunScore(RunOutcome Outcome, string? Reason, long LatencyMs);

internal static class Attribution
{
    public static RunScore Score(EvalCase c, TurnDto t)
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
                return new RunScore(RunOutcome.WrongToolData, null, t.LatencyMs);
        }

        if (!FactMatcher.AllMatch(c.AnswerFacts, t.Text))
            return new RunScore(RunOutcome.WrongAnswer, null, t.LatencyMs);

        return new RunScore(RunOutcome.Pass, null, t.LatencyMs);
    }
}
