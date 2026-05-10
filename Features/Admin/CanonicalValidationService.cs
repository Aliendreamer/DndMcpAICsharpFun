using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin;

public sealed class CanonicalValidationService(
    CanonicalJsonLoader loader,
    EntityReferenceResolver resolver,
    IOptions<EntityExtractionOptions> options,
    ILogger<CanonicalValidationService> logger)
{
    private readonly EntityExtractionOptions _opts = options.Value;

    public async Task<CanonicalValidationReport> ValidateAsync(CancellationToken ct)
    {
        var failures = new List<CanonicalValidationFailure>();
        var warnings = new List<CanonicalValidationWarning>();
        var needsReviewWarnings = new List<CanonicalNeedsReviewWarning>();
        var allEntities = new List<EntityEnvelope>();
        var seenIds = new Dictionary<string, string>(StringComparer.Ordinal);  // id → file

        if (!Directory.Exists(_opts.CanonicalDirectory))
            return new CanonicalValidationReport(0, 0, failures, warnings);

        var files = Directory.GetFiles(_opts.CanonicalDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".errors.json", StringComparison.Ordinal)
                     && !f.EndsWith(".warnings.json", StringComparison.Ordinal))
            .OrderBy(f => f)
            .ToList();

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var loaded = await loader.LoadAsync(path, ct);

                foreach (var entity in loaded.Entities)
                {
                    if (seenIds.TryGetValue(entity.Id, out var existingFile))
                    {
                        failures.Add(new CanonicalValidationFailure(
                            File: Path.GetFileName(path),
                            Kind: "duplicate_id",
                            Detail: $"id '{entity.Id}' also defined in {Path.GetFileName(existingFile)}"));
                    }
                    else
                    {
                        seenIds[entity.Id] = path;
                    }
                }

                var reviewCount = loaded.Entities.Count(e => e.NeedsReview);
                if (reviewCount > 0)
                    needsReviewWarnings.Add(new CanonicalNeedsReviewWarning(
                        File: Path.GetFileName(path),
                        Count: reviewCount));

                allEntities.AddRange(loaded.Entities);
            }
            catch (CanonicalJsonSchemaException ex)
            {
                failures.Add(new CanonicalValidationFailure(
                    File: Path.GetFileName(path),
                    Kind: "schema_validation_failure",
                    Detail: ex.Message));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load canonical JSON file {Path}", path);
                failures.Add(new CanonicalValidationFailure(
                    File: Path.GetFileName(path),
                    Kind: "load_error",
                    Detail: ex.Message));
            }
        }

        var refWarnings = resolver.Resolve(allEntities).ToList();
        foreach (var w in refWarnings)
        {
            var sourceBookSlug = w.SourceEntityId.Split('.')[0];
            var targetBookSlug = w.MissingTargetId.Split('.')[0];
            if (string.Equals(sourceBookSlug, targetBookSlug, StringComparison.Ordinal))
            {
                failures.Add(new CanonicalValidationFailure(
                    File: $"{sourceBookSlug}.json",
                    Kind: "intra_book_dangling_ref_post_extraction",
                    Detail: $"{w.SourceEntityId} references missing intra-book {w.MissingTargetId} at {w.FieldPath}"));
            }
            else
            {
                warnings.Add(new CanonicalValidationWarning(
                    File: $"{sourceBookSlug}.json",
                    SourceEntityId: w.SourceEntityId,
                    FieldPath: w.FieldPath,
                    MissingTargetId: w.MissingTargetId));
            }
        }

        return new CanonicalValidationReport(files.Count, allEntities.Count, failures, warnings, needsReviewWarnings);
    }
}
