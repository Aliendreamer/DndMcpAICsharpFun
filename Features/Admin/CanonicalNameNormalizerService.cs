using System.Text.Json;
using System.Text.RegularExpressions;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin;

public sealed class CanonicalNameNormalizerService(
    CanonicalJsonLoader loader,
    IOptions<EntityExtractionOptions> options)
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static readonly HashSet<string> SmallWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "but", "or", "for", "nor",
        "on", "at", "to", "in", "of",
    };

    private static readonly Regex ApostropheUpperS = new(@"'[A-Z]", RegexOptions.Compiled);

    private readonly EntityExtractionOptions _opts = options.Value;

    public static string DndTitleCase(string name)
    {
        var parts = name.Split(' ');
        var result = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Contains('-'))
            {
                var sub = part.Split('-');
                for (int j = 0; j < sub.Length; j++)
                    sub[j] = ConvertWord(sub[j], i == 0 && j == 0);
                result[i] = string.Join('-', sub);
            }
            else
            {
                result[i] = ConvertWord(part, i == 0);
            }
        }
        return string.Join(' ', result);
    }

    private static string ConvertWord(string word, bool isFirst)
    {
        if (string.IsNullOrEmpty(word)) return word;
        var low = word.ToLowerInvariant();
        if (!isFirst && SmallWords.Contains(low)) return low;
        var cap = char.ToUpperInvariant(low[0]) + low[1..];
        return ApostropheUpperS.Replace(cap, m => m.Value.ToLowerInvariant());
    }

    public async Task<CanonicalNameNormalizerReport> NormalizeAsync(bool dryRun, CancellationToken ct)
    {
        var dir = _opts.CanonicalDirectory;
        if (!Directory.Exists(dir))
            return new CanonicalNameNormalizerReport(0, 0, dryRun, []);

        var files = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".errors.json", StringComparison.Ordinal)
                     && !f.EndsWith(".warnings.json", StringComparison.Ordinal)
                     && !f.EndsWith(".progress.json", StringComparison.Ordinal)
                     && !f.EndsWith(".progress.errors.json", StringComparison.Ordinal))
            .OrderBy(f => f)
            .ToList();

        var changes = new List<CanonicalNameNormalizerFileResult>();
        int totalEntities = 0;

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            var loaded = await loader.LoadAsync(path, ct);
            int titleCased = 0, flagged = 0, unchanged = 0;

            var normalized = loaded.Entities.Select(entity =>
            {
                var name = entity.Name;
                bool isAllCaps = name.Length > 1
                    && name == name.ToUpperInvariant()
                    && name.Any(char.IsLetter);

                // Check for non-all-caps artifacts by passing the lowercased name;
                // the all-caps heuristic won't fire on a lowercase string.
                bool hasOtherArtifacts = ExtractionNeedsReview.HasOcrArtifacts(name.ToLowerInvariant());

                if (isAllCaps && !hasOtherArtifacts)
                {
                    titleCased++;
                    return entity with { Name = DndTitleCase(name) };
                }

                if (ExtractionNeedsReview.HasOcrArtifacts(name))
                {
                    flagged++;
                    return entity with { NeedsReview = true };
                }

                unchanged++;
                return entity;
            }).ToList();

            totalEntities += normalized.Count;
            changes.Add(new CanonicalNameNormalizerFileResult(
                File: Path.GetFileName(path),
                TitleCased: titleCased,
                Flagged: flagged,
                Unchanged: unchanged));

            if (!dryRun)
            {
                var updated = loaded with { Entities = normalized };
                await using var stream = File.Create(path);
                await JsonSerializer.SerializeAsync(stream, updated, WriteOptions, ct);
                await stream.WriteAsync("\n"u8.ToArray(), ct);
            }
        }

        return new CanonicalNameNormalizerReport(files.Count, totalEntities, dryRun, changes);
    }
}
