using System.Diagnostics;

namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

internal sealed record CaseResult(EvalCase Case, IReadOnlyList<RunScore> Runs)
{
    public bool Passed => Runs.Count(r => r.Outcome == RunOutcome.Pass) * 2 > Runs.Count;
}

internal static class FullChainRunner
{
    public static async Task<(string Model, IReadOnlyList<CaseResult> Results)> RunAsync(
        IReadOnlyList<EvalCase> cases, int runs, IEvalTurnClient client, CancellationToken ct)
    {
        var model = "unknown";
        var results = new List<CaseResult>();
        TurnDto? preflight = null;

        if (cases.Count > 0 && runs > 0)
        {
            try
            {
                preflight = await client.RunAsync(cases[0].Prompt, ct);
                model = preflight.Model;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EvalInfraException($"Pre-flight failed: {ex.Message}", ex);
            }
        }

        for (var ci = 0; ci < cases.Count; ci++)
        {
            var c = cases[ci];
            var scores = new List<RunScore>();
            for (var r = 0; r < runs; r++)
            {
                if (ci == 0 && r == 0 && preflight is not null)
                {
                    scores.Add(Attribution.Score(c, preflight));
                    continue;
                }

                var sw = Stopwatch.StartNew();
                try
                {
                    var turn = await client.RunAsync(c.Prompt, ct);
                    if (model == "unknown") model = turn.Model;
                    scores.Add(Attribution.Score(c, turn));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    scores.Add(new RunScore(RunOutcome.InfraError, ex.Message, sw.ElapsedMilliseconds, RunDetail.Infra(ex.Message)));
                }
            }
            results.Add(new CaseResult(c, scores));
        }

        return (model, results);
    }
}
