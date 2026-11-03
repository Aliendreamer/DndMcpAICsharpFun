namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

/// <summary>Entry point for <c>--mode full</c>. Exit codes: 0 OK, 2 regression, 1 infra/setup error.</summary>
internal static class FullModeRunner
{
    public static async Task<int> RunAsync(EvalArgs a)
    {
        if (string.IsNullOrWhiteSpace(a.AdminKey))
        {
            Console.Error.WriteLine("Missing admin key: pass --admin-key or set ADMIN_API_KEY.");
            return 1;
        }

        var canonicalDir = Path.GetFullPath(a.CanonicalDir);
        if (!Directory.Exists(canonicalDir))
        {
            Console.Error.WriteLine($"Canonical dir not found: {canonicalDir}. Run from the repo root (or pass --canonical-dir).");
            return 1;
        }

        var books = (a.Books ?? "phb14,mm14").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var categories = a.Categories?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var seed = a.Seed ?? 42;

        List<EvalCase> cases;
        EvalReport? explicitBaseline = null;
        try
        {
            cases = BuildCases(a, canonicalDir, books, categories, seed);
            if (a.Baseline is { } bp)
                explicitBaseline = EvalReportStore.Load(Path.GetFullPath(bp))
                    ?? throw new InvalidDataException($"Baseline report is empty: {bp}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException
            or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Setup error: {ex.Message}");
            return 1;
        }

        var hash = CaseGenerator.CaseSetHash(cases, books);
        Console.Error.WriteLine($"[full] {cases.Count} cases x {a.Runs} runs, seed {seed}, books {string.Join(",", books)}");

        using var http = new HttpClient { BaseAddress = new Uri(a.Host) };
        var client = new ProgressClient(new EvalTurnClient(http, a.AdminKey), cases, a.Runs);

        string model;
        IReadOnlyList<CaseResult> results;
        try
        {
            (model, results) = await FullChainRunner.RunAsync(cases, a.Runs, client, CancellationToken.None);
        }
        catch (EvalInfraException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var report = EvalReport.Build(model, DateTime.UtcNow, seed, books, hash, results);
        var path = EvalReportStore.Write(report, Path.GetFullPath(a.ReportsDir));
        var baseline = explicitBaseline
            ?? EvalReportStore.LatestBefore(Path.GetFullPath(a.ReportsDir), path);
        var comparison = BaselineComparer.Compare(baseline, report);

        Print(report, baseline, comparison, path);
        return BaselineComparer.ExitCode(comparison, report);
    }

    /// <summary>Generates + loads cases, applies the category filter to manual cases, rejects duplicate ids.</summary>
    internal static List<EvalCase> BuildCases(
        EvalArgs a, string canonicalDir, string[] books, string[]? categories, int seed)
    {
        var opts = new GenOptions(seed, a.Count ?? 5, books, categories);
        var manual = ManualCases.Load(Path.GetFullPath(a.ManualPath));
        if (categories is { Length: > 0 })
            manual = manual.Where(c => categories.Contains(c.Category, StringComparer.OrdinalIgnoreCase)).ToList();

        var cases = CaseGenerator.Generate(canonicalDir, opts).Concat(manual).ToList();
        var dup = cases.GroupBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
            throw new InvalidDataException($"Duplicate case id '{dup.Key}' across generated and manual cases.");
        return cases;
    }

    private static void Print(EvalReport r, EvalReport? baseline, Comparison c, string path)
    {
        Console.WriteLine($"Model {r.Model} | seed {r.Seed} | cases {r.Cases.Count} | hash {r.CaseSetHash[..12]}");
        Console.WriteLine(c.HasBaseline
            ? $"Baseline: {baseline!.Model} @ {baseline.UtcTimestamp:u}{(c.SameCaseSet ? "" : " (different case set; shared ids only)")}"
            : "Baseline: none");
        if (r.InfraErrors > 0)
            Console.WriteLine($"*** {r.InfraErrors} infra errors — results not trustworthy (not used as a baseline) ***");
        Console.WriteLine();
        Console.WriteLine($"{"category",-12} {"passed",-9} {"delta",-7} flag");
        foreach (var cat in r.TotalByCategory.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var delta = c.ByCategory.TryGetValue(cat, out var bc) ? (bc.Current - bc.Baseline).ToString("+0;-0;0") : "-";
            var flag = c.RegressedCategories.Contains(cat) ? "REGRESSION" : "";
            Console.WriteLine($"{cat,-12} {r.PassedByCategory.GetValueOrDefault(cat)}/{r.TotalByCategory[cat],-7} {delta,-7} {flag}");
        }

        Console.WriteLine();
        if (c.NewlyFailing.Count > 0) Console.WriteLine($"Newly failing: {string.Join(", ", c.NewlyFailing)}");
        if (c.NewlyPassing.Count > 0) Console.WriteLine($"Newly passing: {string.Join(", ", c.NewlyPassing)}");

        Console.WriteLine("Failure labels:");
        foreach (var g in r.Cases.GroupBy(x => x.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var labels = g.SelectMany(x => x.Outcomes).Where(o => o != "pass")
                .GroupBy(o => o).OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{x.Key}={x.Count()}");
            var text = string.Join(", ", labels);
            Console.WriteLine($"  {g.Key}: {(text.Length == 0 ? "none" : text)}");
        }

        Console.WriteLine();
        Console.WriteLine($"Report: {path}");
    }

    /// <summary>Prints per-run progress to stderr; relies on the runner calling cases/runs sequentially in order.</summary>
    private sealed class ProgressClient(IEvalTurnClient inner, IReadOnlyList<EvalCase> cases, int runs) : IEvalTurnClient
    {
        private int _call;

        public Task<TurnDto> RunAsync(string prompt, CancellationToken ct)
        {
            // The pre-flight call is case 0 run 0 (the runner reuses it), so calls map linearly onto case/run order.
            var idx = _call++;
            var ci = Math.Min(idx / Math.Max(runs, 1), cases.Count - 1);
            var ri = idx % Math.Max(runs, 1);
            Console.Error.WriteLine($"[{cases[ci].Id}] run {ri + 1}/{runs}...");
            return inner.RunAsync(prompt, ct);
        }
    }
}
