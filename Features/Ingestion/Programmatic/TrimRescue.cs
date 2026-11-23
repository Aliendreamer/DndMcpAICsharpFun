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

    public static bool IsRescueId(string id) =>
        id.EndsWith(Suffix, StringComparison.Ordinal) || id.EndsWith(Suffix + "-2", StringComparison.Ordinal);

    public static string RescueId(string hostId, IReadOnlySet<string> existingIds)
    {
        var parts = hostId.Split('.');
        if (parts.Length > 1) parts[1] = "lore";
        var id = string.Join('.', parts) + Suffix;
        return existingIds.Contains(id) ? id + "-2" : id;
    }

    public static IReadOnlyList<EntityEnvelope> Compute(
        IReadOnlyList<(EntityEnvelope Host, string BaseText)> trimmedHosts,
        IReadOnlyList<EntityEnvelope> finalEntities)
    {
        var held = finalEntities
            .Where(e => e.Disposition != EntityDisposition.Declined && !IsRescueId(e.Id))
            .Select(e => Reduce(e.CanonicalText))
            .ToList();
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
                if (held.Any(h => h.Contains(reduced, StringComparison.Ordinal))) continue;
                if (IsTypoFixed(baseLines[i], hostLines)) continue;
                removed.Add(i);
            }
            if (removed.Count == 0) continue;

            var entries = BuildEntries(baseLines, removed);
            var name = HeadingAbove(baseLines, removed[0]) ?? $"{host.Name} (trimmed text)";
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

    private static string? HeadingAbove(string[] lines, int firstRescued)
    {
        if (firstRescued == 0) return null;
        var line = lines[firstRescued - 1];
        var reduced = Reduce(line);
        return reduced.Length is > 0 and < MinReducedLength && !line.EndsWith('.') ? line : null;
    }

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
