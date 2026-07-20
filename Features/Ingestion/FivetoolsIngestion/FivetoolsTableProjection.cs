using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Builds a book's full CanonicalTable set from the local 5etools data for a given source key.</summary>
public sealed class FivetoolsTableProjection
{
    private static readonly (string File, string Key)[] EmbeddedSources =
    [
        ("races.json", "race"), ("feats.json", "feat"), ("backgrounds.json", "background"),
        ("optionalfeatures.json", "optionalfeature"), ("charcreationoptions.json", "charcreationoption"),
    ];

    public IReadOnlyList<CanonicalTable> BuildForBook(string fivetoolsDir, string sourceKey)
    {
        var tables = new List<CanonicalTable>();

        // Class progression tables (run first so they keep the bare id on any collision).
        foreach (var classFile in SafeGlob(Path.Combine(fivetoolsDir, "class"), "class-*.json"))
            foreach (var cls in Entries(classFile, "class", sourceKey))
                tables.Add(ClassProgressionTableProjector.Project(cls, sourceKey));

        // Captioned embedded tables from class + subclass entries.
        foreach (var classFile in SafeGlob(Path.Combine(fivetoolsDir, "class"), "class-*.json"))
            foreach (var arrayKey in new[] { "class", "subclass" })
                foreach (var e in Entries(classFile, arrayKey, sourceKey))
                    tables.AddRange(CaptionedTableProjector.Project(e, sourceKey, PageOf(e)));

        // Captioned embedded tables from the top-level category files.
        foreach (var (file, key) in EmbeddedSources)
            foreach (var e in Entries(Path.Combine(fivetoolsDir, file), key, sourceKey))
                tables.AddRange(CaptionedTableProjector.Project(e, sourceKey, PageOf(e)));

        return Deduplicate(tables);
    }

    private static IEnumerable<string> SafeGlob(string dir, string pattern) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern) : [];

    private static IEnumerable<JsonElement> Entries(string path, string arrayKey, string sourceKey)
    {
        if (!File.Exists(path)) yield break;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty(arrayKey, out var arr) || arr.ValueKind != JsonValueKind.Array) yield break;
        foreach (var e in arr.EnumerateArray())
            if (e.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() == sourceKey)
                yield return e.Clone(); // Clone so it outlives the JsonDocument
    }

    private static int? PageOf(JsonElement e) =>
        e.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

    private static IReadOnlyList<CanonicalTable> Deduplicate(List<CanonicalTable> tables)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CanonicalTable>(tables.Count);
        foreach (var t in tables)
        {
            var id = t.Id;
            for (var n = 2; !seen.Add(id); n++) id = $"{t.Id}-{n}";
            result.Add(id == t.Id ? t : t with { Id = id });
        }
        return result;
    }
}
