using System.Text;
using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

/// <summary>
/// Deterministic spell↔class join (spell-class-join). Loads the 5etools reverse index
/// <c>&lt;fivetoolsDir&gt;/spells/sources.json</c> — <c>{ SOURCE: { SpellName: { class:[{name}] } } }</c> —
/// once, and answers whether a class can cast a spell. Names/sources are normalized
/// (lowercase alphanumerics) so entity names match the 5etools keys. A missing file yields an empty
/// index (no relationships, no throw), so <c>castableByClass</c> degrades to an empty honest set.
/// </summary>
public sealed class SpellClassIndex
{
    private readonly Dictionary<(string Name, string Source), HashSet<string>> _bySpellSource = new();
    private readonly Dictionary<string, HashSet<string>> _byName = new(StringComparer.Ordinal);

    public SpellClassIndex(string fivetoolsDir)
    {
        var path = Path.Combine(fivetoolsDir, "spells", "sources.json");
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            foreach (var sourceProp in doc.RootElement.EnumerateObject())
            {
                if (sourceProp.Value.ValueKind != JsonValueKind.Object) continue;
                var source = Norm(sourceProp.Name);
                foreach (var spellProp in sourceProp.Value.EnumerateObject())
                {
                    var classes = ExtractClasses(spellProp.Value);
                    if (classes.Count == 0) continue;
                    var name = Norm(spellProp.Name);
                    Add(_bySpellSource, (name, source), classes);
                    AddName(name, classes);
                }
            }
        }
        catch (JsonException)
        {
            // Malformed index → leave empty; the join yields an honest empty set rather than erroring.
        }
    }

    /// <summary>The (display-name) casting classes for a spell — source-specific if known, else the
    /// union across all sources bearing that spell name.</summary>
    public IReadOnlyCollection<string> ClassesFor(string name, string? source)
    {
        var n = Norm(name);
        if (source is not null && _bySpellSource.TryGetValue((n, Norm(source)), out var set))
            return set;
        return _byName.TryGetValue(n, out var byName) ? byName : Array.Empty<string>();
    }

    public bool CanCast(string className, string name, string? source)
    {
        var target = Norm(className);
        foreach (var c in ClassesFor(name, source))
            if (Norm(c) == target) return true;
        return false;
    }

    private static HashSet<string> ExtractClasses(JsonElement info)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (info.ValueKind != JsonValueKind.Object) return result;
        foreach (var arrKey in new[] { "class", "classVariant" })
        {
            if (info.TryGetProperty(arrKey, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in arr.EnumerateArray())
                {
                    if (c.ValueKind == JsonValueKind.Object
                        && c.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                        result.Add(n.GetString()!); // keep the display name
                }
            }
        }
        return result;
    }

    private static void Add(Dictionary<(string, string), HashSet<string>> map, (string, string) key, HashSet<string> classes)
    {
        if (!map.TryGetValue(key, out var set)) map[key] = set = new HashSet<string>(StringComparer.Ordinal);
        set.UnionWith(classes);
    }

    private void AddName(string name, HashSet<string> classes)
    {
        if (!_byName.TryGetValue(name, out var set)) _byName[name] = set = new HashSet<string>(StringComparer.Ordinal);
        set.UnionWith(classes);
    }

    private static string Norm(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
