using System.Text.Json;

namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

/// <param name="Outcomes">Per-run labels: pass, wrong-tool:not-offered, wrong-tool:not-chosen, bind-error,
/// wrong-tool-data, wrong-answer, infra-error.</param>
internal sealed record CaseReport(
    string Id, string Category, string Tag, int Passes, int Runs, bool Passed, IReadOnlyList<string> Outcomes);

internal sealed record EvalReport(
    string Mode,
    string Model,
    DateTime UtcTimestamp,
    int Seed,
    IReadOnlyList<string> Books,
    string CaseSetHash,
    IReadOnlyList<CaseReport> Cases,
    IReadOnlyDictionary<string, int> PassedByCategory,
    IReadOnlyDictionary<string, int> TotalByCategory)
{
    public static EvalReport Build(
        string model, DateTime utcNow, int seed, IReadOnlyList<string> books, string caseSetHash,
        IReadOnlyList<CaseResult> results)
    {
        var cases = results.Select(r => new CaseReport(
            r.Case.Id, r.Case.Category, r.Case.Tag,
            r.Runs.Count(s => s.Outcome == RunOutcome.Pass), r.Runs.Count, r.Passed,
            r.Runs.Select(Label).ToList())).ToList();
        return new EvalReport("full", model, utcNow, seed, books, caseSetHash, cases,
            cases.GroupBy(c => c.Category).ToDictionary(g => g.Key, g => g.Count(c => c.Passed)),
            cases.GroupBy(c => c.Category).ToDictionary(g => g.Key, g => g.Count()));
    }

    public static string Label(RunScore s) => s.Outcome switch
    {
        RunOutcome.Pass => "pass",
        RunOutcome.WrongTool => $"wrong-tool:{s.Reason}",
        RunOutcome.BindError => "bind-error",
        RunOutcome.WrongToolData => "wrong-tool-data",
        RunOutcome.WrongAnswer => "wrong-answer",
        _ => "infra-error",
    };
}

internal static class EvalReportStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string Write(EvalReport r, string dir)
    {
        Directory.CreateDirectory(dir);
        var name = $"{r.UtcTimestamp:yyyyMMdd-HHmmss}-{SanitizeModel(r.Model)}-full.json";
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, JsonSerializer.Serialize(r, Json));
        return path;
    }

    public static string SanitizeModel(string m) =>
        string.Concat(m.ToLowerInvariant().Select(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-' ? c : '_'));

    public static EvalReport? Load(string path) =>
        JsonSerializer.Deserialize<EvalReport>(File.ReadAllText(path), Json);

    public static EvalReport? LatestBefore(string dir, string excludePath)
    {
        if (!Directory.Exists(dir)) return null;
        var exclude = Path.GetFullPath(excludePath);
        var latest = Directory.GetFiles(dir, "*-full.json")
            .Where(f => Path.GetFullPath(f) != exclude)
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            .FirstOrDefault();
        return latest is null ? null : Load(latest);
    }
}
