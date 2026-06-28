using System.Text.Json;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin;

public sealed class CanonicalNameNormalizerService(
    CanonicalJsonLoader loader,
    IOptions<EntityExtractionOptions> options)
{
    private readonly EntityExtractionOptions _opts = options.Value;

    public static string DndTitleCase(string name) => EntityNameNormalizer.TitleCase(name);


    public async Task<CanonicalNameNormalizerReport> NormalizeAsync(bool dryRun, CancellationToken ct)
    {
        var dir = _opts.CanonicalDirectory;
        if (!Directory.Exists(dir))
            return new CanonicalNameNormalizerReport(0, 0, dryRun, []);

        var files = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            .Where(f => !CanonicalSidecarFiles.IsSidecar(f))
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
                    return entity with { Name = EntityNameNormalizer.TitleCase(name), NeedsReview = false };
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
                await JsonSerializer.SerializeAsync(stream, updated, CanonicalJson.WriteOptions, ct);
                await stream.WriteAsync("\n"u8.ToArray(), ct);
            }
        }

        return new CanonicalNameNormalizerReport(files.Count, totalEntities, dryRun, changes);
    }
}
