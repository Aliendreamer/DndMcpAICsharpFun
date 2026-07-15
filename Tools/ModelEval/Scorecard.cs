using System.Text;

namespace DndMcpAICsharpFun.Tools.ModelEval;

internal static class Scorecard
{
    public static void Print(EvalArgs a, IReadOnlyList<(Scenario Scenario, IReadOnlyList<RunResult> Runs)> results)
    {
        var n = a.Runs;
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"=== Scorecard — model={a.Model} think={(a.ThinkOn ? "on" : "off")} runs={n} ===");
        sb.AppendLine($"{"scenario",-18} {"expect",-20} {"sel",-6} {"bind",-6} {"adhere",-7} {"nolist",-7} {"numlbl",-7} {"p50ms",-8} {"p95ms",-8}");

        var totSel = 0; var totBind = 0; var totAdhere = 0; var denom = 0;
        var totNoList = 0; var noListDenom = 0;
        var totNumLbl = 0; var numLblDenom = 0;

        foreach (var (scenario, runs) in results)
        {
            var expected = scenario.ExpectedTool ?? "(none)";
            var sel = runs.Count(r => scenario.ExpectedTool is null ? r.SelectedTool is null : r.SelectedTool == scenario.ExpectedTool);
            var bind = runs.Count(r => r.BindOk || scenario.ExpectedTool is null);
            var adhere = runs.Count(r => r.Adhered);
            var p50 = Percentile(runs.Select(r => r.WallMs).ToList(), 50);
            var p95 = Percentile(runs.Select(r => r.WallMs).ToList(), 95);

            var noListApplicable = runs.Any(r => r.NoListOk.HasValue);
            var noListCell = "-";
            if (noListApplicable)
            {
                var noListTally = runs.Count(r => r.NoListOk == true);
                noListCell = noListTally + "/" + n;
                totNoList += noListTally; noListDenom += n;
            }

            var numLblApplicable = runs.Any(r => r.NumberLabelOk.HasValue);
            var numLblCell = "-";
            if (numLblApplicable)
            {
                var numLblTally = runs.Count(r => r.NumberLabelOk == true);
                numLblCell = numLblTally + "/" + n;
                totNumLbl += numLblTally; numLblDenom += n;
            }

            sb.AppendLine($"{scenario.Name,-18} {expected,-20} {sel + "/" + n,-6} {bind + "/" + n,-6} {adhere + "/" + n,-7} {noListCell,-7} {numLblCell,-7} {p50,-8} {p95,-8}");
            totSel += sel; totBind += bind; totAdhere += adhere; denom += n;
        }

        sb.AppendLine(new string('-', 90));
        sb.AppendLine($"{"TOTAL",-18} {"",-20} {totSel + "/" + denom,-6} {totBind + "/" + denom,-6} {totAdhere + "/" + denom,-7} {totNoList + "/" + noListDenom,-7} {totNumLbl + "/" + numLblDenom,-7}");
        Console.WriteLine(sb.ToString());
    }

    private static long Percentile(List<long> values, int p)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        var idx = (int)Math.Ceiling(p / 100.0 * values.Count) - 1;
        return values[Math.Clamp(idx, 0, values.Count - 1)];
    }
}
