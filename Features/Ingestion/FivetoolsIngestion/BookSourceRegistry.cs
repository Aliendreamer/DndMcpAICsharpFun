using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

public sealed record FivetoolsBookInfo(
    string SourceKey,
    string Name,
    string Group,
    int PublishedYear,
    string DisplayAbbr);

public sealed class BookSourceRegistry
{
    private readonly IReadOnlyDictionary<string, FivetoolsBookInfo> _byKey;
    private readonly IReadOnlyList<FivetoolsBookInfo> _all;

    public BookSourceRegistry(string booksJsonPath = "5etools/books.json")
    {
        if (!File.Exists(booksJsonPath))
        {
            _byKey = new Dictionary<string, FivetoolsBookInfo>(StringComparer.OrdinalIgnoreCase);
            _all = Array.Empty<FivetoolsBookInfo>();
            return;
        }

        using var stream = File.OpenRead(booksJsonPath);
        using var doc = JsonDocument.Parse(stream);
        var entries = new List<FivetoolsBookInfo>();

        foreach (var b in doc.RootElement.GetProperty("book").EnumerateArray())
        {
            var id = b.GetProperty("id").GetString()!;
            var name = b.GetProperty("name").GetString()!;
            var group = b.TryGetProperty("group", out var g) ? g.GetString()! : "other";
            var published = b.TryGetProperty("published", out var p) ? p.GetString()! : "2000-01-01";
            var year = int.Parse(published.AsSpan(0, 4));
            entries.Add(new FivetoolsBookInfo(id, name, group, year, ComputeDisplayAbbr(id, year)));
        }

        _byKey = entries.ToDictionary(e => e.SourceKey, StringComparer.OrdinalIgnoreCase);
        _all = entries;
    }

    public FivetoolsBookInfo? TryGetBook(string sourceKey)
        => _byKey.TryGetValue(sourceKey, out var info) ? info : null;

    public IReadOnlyList<FivetoolsBookInfo> GetAll() => _all;

    public IReadOnlyList<string> GetByGroup(string group)
        => _all.Where(b => string.Equals(b.Group, group, StringComparison.OrdinalIgnoreCase))
               .Select(b => b.SourceKey).ToList();

    public IReadOnlyList<string> ResolveIntent(string intent)
        => intent.Trim().ToLowerInvariant() switch
        {
            "core" or "core books" => GetByGroup("core"),
            "supplement" or "supplements" => GetByGroup("supplement"),
            "setting" or "settings" => GetByGroup("setting"),
            "2014" or "5e" => _all.Where(b => b.PublishedYear < 2020).Select(b => b.SourceKey).ToList(),
            "2024" or "5.5e" => _all.Where(b => b.PublishedYear >= 2024).Select(b => b.SourceKey).ToList(),
            "srd" or "free rules" => (IReadOnlyList<string>)["srd52"],
            _ => Array.Empty<string>()
        };

    public IReadOnlyList<string> SuggestByName(string displayName, int top = 3)
        => _all
            .Select(b => (b.SourceKey, Score: NameSimilarity(displayName, b.Name)))
            .Where(x => x.Score > 0.2)
            .OrderByDescending(x => x.Score)
            .Take(top)
            .Select(x => x.SourceKey)
            .ToList();

    private static double NameSimilarity(string a, string b)
    {
        var wordsA = Tokenize(a).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wordsB = Tokenize(b).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var intersection = wordsA.Count(w => wordsB.Contains(w));
        var union = wordsA.Union(wordsB, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static IEnumerable<string> Tokenize(string s)
        => s.ToLowerInvariant().Split([' ', '-', '\'', '(', ')', ':'], StringSplitOptions.RemoveEmptyEntries);

    private static string ComputeDisplayAbbr(string sourceKey, int year)
    {
        var key = sourceKey.StartsWith('X') ? sourceKey[1..] : sourceKey;
        return $"{key}'{year % 100:D2}";
    }
}