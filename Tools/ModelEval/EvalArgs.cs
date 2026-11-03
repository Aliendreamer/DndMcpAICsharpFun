namespace DndMcpAICsharpFun.Tools.ModelEval;

internal sealed record EvalArgs(
    string Model,
    bool ThinkOn,
    int Runs,
    string BaseUrl,
    string? PersonaPath,
    string Mode = "stub",
    string Host = "http://localhost:5101",
    string? AdminKey = null,
    int? Seed = null,
    int? Count = null,
    string? Books = null,
    string? Categories = null,
    string? Baseline = null,
    string CanonicalDir = "books/canonical",
    string ReportsDir = "Tools/ModelEval/reports")
{
    public static EvalArgs Parse(string[] args)
    {
        var model = "qwen3:8b";
        var thinkOn = true;
        int? runs = null;
        var baseUrl = "http://localhost:11434";
        var personaPath = (string?)null;
        var mode = "stub";
        var host = "http://localhost:5101";
        string? adminKey = null;
        int? seed = null;
        int? count = null;
        string? books = null;
        string? categories = null;
        string? baseline = null;
        var canonicalDir = "books/canonical";
        var reportsDir = "Tools/ModelEval/reports";

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--model": model = args[++i]; break;
                case "--think": thinkOn = !string.Equals(args[++i], "off", StringComparison.OrdinalIgnoreCase); break;
                case "--runs": runs = int.Parse(args[++i]); break;
                case "--base-url": baseUrl = args[++i]; break;
                case "--persona": personaPath = args[++i]; break;
                case "--mode": mode = args[++i].ToLowerInvariant(); break;
                case "--host": host = args[++i]; break;
                case "--admin-key": adminKey = args[++i]; break;
                case "--seed": seed = int.Parse(args[++i]); break;
                case "--count": count = int.Parse(args[++i]); break;
                case "--books": books = args[++i]; break;
                case "--categories": categories = args[++i]; break;
                case "--baseline": baseline = args[++i]; break;
                case "--canonical-dir": canonicalDir = args[++i]; break;
                case "--reports-dir": reportsDir = args[++i]; break;
            }
        }

        adminKey ??= Environment.GetEnvironmentVariable("ADMIN_API_KEY");
        var effectiveRuns = runs ?? (mode == "full" ? 3 : 5);

        return new EvalArgs(model, thinkOn, effectiveRuns, baseUrl, personaPath, mode, host, adminKey,
            seed, count, books, categories, baseline, canonicalDir, reportsDir);
    }
}
