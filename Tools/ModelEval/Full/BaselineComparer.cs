namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

internal sealed record Comparison(
    bool HasBaseline,
    bool SameCaseSet,
    IReadOnlyDictionary<string, (int Baseline, int Current)> ByCategory,
    IReadOnlyList<string> NewlyFailing,
    IReadOnlyList<string> NewlyPassing,
    IReadOnlyList<string> RegressedCategories);

internal static class BaselineComparer
{
    public static Comparison Compare(EvalReport? baseline, EvalReport current)
    {
        if (baseline is null)
            return new Comparison(false, true, new Dictionary<string, (int, int)>(), [], [], []);

        var baseById = baseline.Cases.ToDictionary(c => c.Id);
        var shared = current.Cases.Where(c => baseById.ContainsKey(c.Id)).ToList();

        var byCategory = shared.GroupBy(c => c.Category).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (Baseline: g.Count(c => baseById[c.Id].Passed), Current: g.Count(c => c.Passed)));
        var newlyFailing = shared.Where(c => baseById[c.Id].Passed && !c.Passed).Select(c => c.Id).ToList();
        var newlyPassing = shared.Where(c => !baseById[c.Id].Passed && c.Passed).Select(c => c.Id).ToList();
        var regressed = byCategory.Where(kv => kv.Value.Baseline - kv.Value.Current > 1).Select(kv => kv.Key).ToList();

        var sameSet = baseline.Cases.Count == current.Cases.Count && shared.Count == current.Cases.Count;
        return new Comparison(true, sameSet, byCategory, newlyFailing, newlyPassing, regressed);
    }

    public static int ExitCode(Comparison c) => c.RegressedCategories.Count > 0 ? 2 : 0;
}
