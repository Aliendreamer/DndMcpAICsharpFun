using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

/// <summary>
/// Keeps printed text that a <c>canonicalText</c> override removed from an entity and that no other entity holds,
/// as a flagged Lore "rescue" entity so the review can keep or decline it.
/// </summary>
public static partial class TrimRescue
{
    public const string Suffix = "-trimmed";
    public const string TextCompleteCheck = "text-complete";

    private const int MinReducedLength = 30;
    private const double TypoKeepRatio = 0.7;

    public static bool IsRescueId(string id) => RescueIdRegex().IsMatch(id);

    public static string RescueId(string hostId, IReadOnlySet<string> existingIds)
    {
        var parts = hostId.Split('.');
        if (parts.Length > 1) parts[1] = "lore";
        var id = string.Join('.', parts) + Suffix;
        if (!existingIds.Contains(id)) return id;
        for (var n = 2; ; n++)
        {
            var candidate = $"{id}-{n}";
            if (!existingIds.Contains(candidate)) return candidate;
        }
    }

    public static IReadOnlyList<EntityEnvelope> Compute(
        IReadOnlyList<(EntityEnvelope Host, string BaseText)> trimmedHosts,
        IReadOnlyList<EntityEnvelope> finalEntities)
    {
        // Reduced texts joined by a non-alphanumeric separator, so a reduced line never matches across two texts.
        var held = string.Join('|', finalEntities
            .Where(e => e.Disposition != EntityDisposition.Declined && !IsRescueId(e.Id))
            .Select(e => Reduce(e.CanonicalText)));
        var ids = finalEntities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var byId = finalEntities.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        var rescues = new List<EntityEnvelope>();

        foreach (var (inputHost, baseText) in trimmedHosts)
        {
            var host = byId.GetValueOrDefault(inputHost.Id, inputHost);
            if (host.Disposition == EntityDisposition.Declined) continue;

            var baseLines = baseText.Split('\n').Select(l => l.Trim()).ToArray();
            var hostLines = host.CanonicalText.Split('\n').Select(WordCounts).ToList();

            var removed = new List<int>();
            for (var i = 0; i < baseLines.Length; i++)
            {
                var reduced = Reduce(baseLines[i]);
                if (reduced.Length < MinReducedLength) continue;
                if (held.Contains(reduced, StringComparison.Ordinal)) continue;
                if (IsTypoFixed(baseLines[i], hostLines)) continue;
                removed.Add(i);
            }
            if (removed.Count == 0) continue;

            var entries = BuildEntries(baseLines, removed);
            var name = HeadingAbove(baseLines, removed[0], held) ?? $"{host.Name} (trimmed text)";
            var id = RescueId(host.Id, ids);
            ids.Add(id);

            var fields = JsonSerializer.SerializeToElement(new { entries });
            rescues.Add(new EntityEnvelope(
                id, EntityType.Lore, name, host.SourceBook, host.Edition, host.Page,
                host.FirstAppearedIn, host.RevisedIn, host.SettingTags,
                name + "\n" + string.Join("\n", entries), fields,
                DataSource: DataSources.Programmatic,
                Disposition: EntityDisposition.NeedsReview,
                Checks: [new EntityCheck(TextCompleteCheck, false,
                    $"text a review override removed from {host.Id}; no other entity holds it")],
                Provenance: host.Provenance));
        }
        return rescues;
    }

    private static List<string> BuildEntries(string[] lines, List<int> removed)
    {
        var entries = new List<string>();
        var sb = new StringBuilder();
        for (var k = 0; k < removed.Count; k++)
        {
            var line = lines[removed[k]];
            var continues = k > 0 && removed[k] == removed[k - 1] + 1;
            if (!continues)
            {
                if (sb.Length > 0) entries.Add(sb.ToString());
                sb.Clear();
                sb.Append(line);
            }
            else if (sb.Length > 0 && sb[^1] == '-')
            {
                sb.Length--;
                sb.Append(line);
            }
            else
            {
                sb.Append(' ').Append(line);
            }
        }
        if (sb.Length > 0) entries.Add(sb.ToString());
        return entries;
    }

    private static string? HeadingAbove(string[] lines, int firstRescued, string held)
    {
        if (firstRescued == 0) return null;
        var line = lines[firstRescued - 1];
        return IsHeading(line) && !held.Contains(Reduce(line), StringComparison.Ordinal) ? line : null;
    }

    private static bool IsHeading(string line)
    {
        if (line.Length == 0 || line.IndexOfAny(HeadingForbidden) >= 0 || line.Any(char.IsDigit)) return false;
        if (line[^1] is '.' or ',' or ':' or ';' or '-') return false;
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 6 || !char.IsUpper(words[0][0])) return false;
        if (StatLabelRegex().IsMatch(line)) return false;
        var allCaps = !line.Any(char.IsLower);
        return allCaps || words.Skip(1).All(w => SmallWords.Contains(w) || !char.IsLetter(w[0]) || char.IsUpper(w[0]));
    }

    private static readonly char[] HeadingForbidden = ['(', ')', '+', '\u2014', '\u2013'];
    private static readonly HashSet<string> SmallWords =
        ["a", "an", "and", "as", "at", "but", "by", "for", "in", "of", "on", "or", "the", "to", "with"];

    [GeneratedRegex(@"^(Armor Class|Hit Points|Speed|STR|DEX|CON|INT|WIS|CHA|Saving Throws|Skills|Damage|Condition|Senses|Languages|Challenge|Proficiency)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StatLabelRegex();

    [GeneratedRegex(@"-trimmed(-\d+)?$")]
    private static partial Regex RescueIdRegex();

    private static bool IsTypoFixed(string line, List<Dictionary<string, int>> hostLines)
    {
        var words = WordCounts(line);
        var total = words.Values.Sum();
        if (total == 0) return false;
        foreach (var hl in hostLines)
        {
            var matched = words.Sum(w => Math.Min(w.Value, hl.GetValueOrDefault(w.Key)));
            if (matched >= TypoKeepRatio * total) return true;
        }
        return false;
    }

    private static Dictionary<string, int> WordCounts(string line)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in WordRegex().Matches(line.ToLowerInvariant()))
        {
            if (m.Length < 3) continue;
            counts[m.Value] = counts.GetValueOrDefault(m.Value) + 1;
        }
        return counts;
    }

    private static string Reduce(string text) => NonAlnumRegex().Replace(text.ToLowerInvariant(), "");

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex WordRegex();

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlnumRegex();
}
