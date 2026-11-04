using System.Collections.Concurrent;
using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Programmatic;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Ingestion;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin.Review;

public sealed record ReviewListItem(
    string Id,
    string Name,
    EntityType Type,
    string BookSlug,
    int? Page,
    bool Flagged,
    IReadOnlyList<string> FailedReasons);

/// <summary>
/// The admin review surface for programmatically extracted entities (canonical files with schema "2").
/// Every write follows one flow: load overrides, change them, save, re-derive the entity in the canonical file,
/// rewrite the file, then re-index that one entity (an entity that is not Accepted is removed from dnd_entities).
/// </summary>
public sealed class EntityReviewService(
    CanonicalJsonLoader loader,
    CanonicalJsonWriter writer,
    OverridesStore overridesStore,
    IEntityIngestionOrchestrator ingestion,
    IBookExtractorClient extractor,
    IIngestionTracker tracker,
    IOptions<EntityIngestionOptions> entityOptions,
    IOptions<IngestionOptions> ingestionOptions)
{
    private const string ProgrammaticSchema = "2";

    // One writer per book file; bounded by the number of registered books.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Located(string Slug, string Path, CanonicalJsonFile File, EntityEnvelope Entity);

    /// <summary>
    /// Checks worth showing the admin: every failed check, plus passed checks that carry a reason
    /// (machine repairs such as <c>ability-repaired</c> / <c>name-repaired</c>), so no repair is invisible.
    /// </summary>
    public static IReadOnlyList<EntityCheck> VisibleChecks(EntityEnvelope e) =>
        e.Checks.Where(c => !c.Passed || !string.IsNullOrWhiteSpace(c.Reason)).ToList();

    public async Task<IReadOnlyList<string>> ListBooksAsync(CancellationToken ct) =>
        (await LoadProgrammaticAsync(null, ct)).Select(f => f.Slug).ToList();

    public async Task<IReadOnlyList<ReviewListItem>> ListAsync(string? bookSlug, bool flaggedOnly, CancellationToken ct)
    {
        var items = new List<ReviewListItem>();
        foreach (var (slug, _, file) in await LoadProgrammaticAsync(bookSlug, ct))
        {
            foreach (var e in file.Entities)
            {
                var flagged = e.Disposition != EntityDisposition.Accepted || e.NeedsReview;
                if (flaggedOnly && !flagged) continue;
                items.Add(new ReviewListItem(e.Id, e.Name, e.Type, slug, e.Page, flagged,
                    e.Checks.Where(c => !c.Passed).Select(c => c.Reason).ToList()));
            }
        }
        return items;
    }

    public async Task<EntityEnvelope?> GetAsync(string entityId, CancellationToken ct) =>
        (await LocateAsync(entityId, ct))?.Entity;

    public async Task<EntityEnvelope> OverrideAsync(
        string entityId, string field, JsonElement value, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        var value0 = value.Clone();
        return await MutateAsync(entityId, (e, o) =>
        {
            if (!OverrideMerger.ValidatePath(e, field))
                throw new ArgumentException($"Invalid field path '{field}'.", nameof(field));

            // The baseline is the extractor's value: the previous override's baseline (rebased to the new
            // extraction if it drifted), or, for a first override, what the entity holds right now.
            var prior = o.Overrides.LastOrDefault(x => x.EntityId == entityId && x.Field == field);
            var baseline = prior is null
                ? OverrideMerger.GetValue(e, field)
                : OverrideMerger.DriftNow(e, prior) ?? prior.Extracted;

            var updated = OverrideMerger.TrySet(e, field, value0, out var why)
                          ?? throw new ArgumentException($"Cannot apply override at '{field}': {why}", nameof(field));
            updated = StripOverrideChecks(updated, field);

            var overrides = o.Overrides.Where(x => !(x.EntityId == entityId && x.Field == field))
                .Append(new FieldOverride(entityId, field, value0, baseline, reason.Trim(), DateTimeOffset.UtcNow))
                .ToList();
            var next = new EntityOverrides(overrides, o.Approvals);
            return (OverrideMerger.SetDisposition(updated, IsApproved(next, entityId)), next);
        }, ct);
    }

    public async Task<EntityEnvelope> ApproveAsync(string entityId, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        return await MutateAsync(entityId, (e, o) =>
        {
            // Approving accepts the current extraction as the baseline, so drift no longer applies.
            var rebased = new List<FieldOverride>();
            var updated = e;
            foreach (var ov in o.Overrides)
            {
                if (ov.EntityId == entityId && OverrideMerger.DriftNow(e, ov) is { } now)
                {
                    rebased.Add(ov with { Extracted = now });
                    updated = StripOverrideChecks(updated, ov.Field, unapplied: false);
                }
                else
                {
                    rebased.Add(ov);
                }
            }

            var approvals = o.Approvals.Where(a => a.EntityId != entityId)
                .Append(new Approval(entityId, reason.Trim(), DateTimeOffset.UtcNow)).ToList();
            return (OverrideMerger.SetDisposition(updated, approved: true), new EntityOverrides(rebased, approvals));
        }, ct);
    }

    /// <summary>
    /// Drops one override and puts the extractor's value back. This is how an <c>override-unapplied</c> override,
    /// which approval cannot waive, is cleared.
    /// </summary>
    public async Task<EntityEnvelope> RemoveOverrideAsync(string entityId, string field, CancellationToken ct)
    {
        return await MutateAsync(entityId, (e, o) =>
        {
            var ov = o.Overrides.LastOrDefault(x => x.EntityId == entityId && x.Field == field)
                     ?? throw new ArgumentException($"No override at '{field}' for {entityId}.", nameof(field));

            var unapplied = e.Checks.Any(c => c.Name == OverrideMerger.UnappliedCheck
                                              && c.Reason.StartsWith($"override at {field} could not", StringComparison.Ordinal));
            var updated = e;
            if (!unapplied && (OverrideMerger.DriftNow(e, ov) ?? ov.Extracted) is { } restore)
                updated = OverrideMerger.TrySet(e, field, restore, out _) ?? e;
            updated = StripOverrideChecks(updated, field);

            var next = new EntityOverrides(
                o.Overrides.Where(x => !(x.EntityId == entityId && x.Field == field)).ToList(), o.Approvals);
            return (OverrideMerger.SetDisposition(updated, IsApproved(next, entityId)), next);
        }, ct);
    }

    /// <summary>The overrides currently saved for one entity (so the page can offer to remove them).</summary>
    public async Task<IReadOnlyList<FieldOverride>> GetOverridesAsync(string entityId, CancellationToken ct)
    {
        var loc = await LocateAsync(entityId, ct);
        if (loc is null) return [];
        var o = await overridesStore.LoadAsync(loc.Slug, ct);
        return o.Overrides.Where(x => x.EntityId == entityId).ToList();
    }

    /// <summary>Pixel size of a PNG data URI (read from its IHDR), so the page can scale provenance boxes to the image.</summary>
    public static bool TryGetPngSize(string dataUri, out int width, out int height)
    {
        width = height = 0;
        const string prefix = "data:image/png;base64,";
        if (!dataUri.StartsWith(prefix, StringComparison.Ordinal)) return false;
        try
        {
            var head = Convert.FromBase64String(dataUri.Substring(prefix.Length, Math.Min(64, dataUri.Length - prefix.Length) / 4 * 4));
            if (head.Length < 24 || head[0] != 0x89 || head[1] != 0x50) return false;
            width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            return width > 0 && height > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public async Task<string> GetPageImageDataUriAsync(string entityId, int page, CancellationToken ct)
    {
        var loc = await LocateAsync(entityId, ct) ?? throw new KeyNotFoundException($"No programmatic entity '{entityId}'.");
        var known = (loc.Entity.Provenance?.Pages.Select(p => p.Page) ?? []).Append(loc.Entity.Page ?? -1);
        if (!known.Contains(page))
            throw new ArgumentException($"Page {page} is not a source page of {entityId}.", nameof(page));

        var record = await FindRecordAsync(loc.Slug, ct)
                     ?? throw new InvalidOperationException($"No ingestion record for book '{loc.Slug}'.");
        var pdfPath = Path.GetRelativePath(ingestionOptions.Value.BooksPath, record.FilePath);
        var bytes = await extractor.GetPageImageAsync(pdfPath, page, ct);
        var mime = bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? "image/jpeg" : "image/png";
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    // ── internals ────────────────────────────────────────────────────────────

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required.", nameof(reason));
    }

    private static bool IsApproved(EntityOverrides o, string entityId) => o.Approvals.Any(a => a.EntityId == entityId);

    /// <summary>Removes the drift (and optionally unapplied) checks that belong to the override at <paramref name="field"/>.</summary>
    private static EntityEnvelope StripOverrideChecks(EntityEnvelope e, string field, bool unapplied = true) =>
        e with
        {
            Checks = e.Checks.Where(c =>
                !(c.Name == OverrideMerger.DriftCheck && c.Reason.Contains($" at {field}:", StringComparison.Ordinal))
                && !(unapplied && c.Name == OverrideMerger.UnappliedCheck
                     && c.Reason.StartsWith($"override at {field} could not", StringComparison.Ordinal))).ToList(),
        };

    private async Task<EntityEnvelope> MutateAsync(
        string entityId,
        Func<EntityEnvelope, EntityOverrides, (EntityEnvelope Entity, EntityOverrides Overrides)> change,
        CancellationToken ct)
    {
        var first = await LocateAsync(entityId, ct) ?? throw new KeyNotFoundException($"No programmatic entity '{entityId}'.");
        var sem = Locks.GetOrAdd(first.Path, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // Re-read under the lock so concurrent saves on the same book never lose each other's changes.
            var loc = await LocateAsync(entityId, ct) ?? throw new KeyNotFoundException($"No programmatic entity '{entityId}'.");
            var record = await FindRecordAsync(loc.Slug, ct)
                         ?? throw new InvalidOperationException($"No ingestion record for book '{loc.Slug}'.");

            var overrides = await overridesStore.LoadAsync(loc.Slug, ct);
            var (entity, nextOverrides) = change(loc.Entity, overrides);

            await overridesStore.SaveAsync(loc.Slug, nextOverrides, ct);
            var entities = loc.File.Entities.Select(x => x.Id == entityId ? entity : x).ToList();
            await writer.WriteAsync(loc.Path, loc.File with { Entities = entities }, ct);
            await ingestion.ReindexEntityAsync(record.Id, entityId, ct);
            return entity;
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<Located?> LocateAsync(string entityId, CancellationToken ct)
    {
        foreach (var (slug, path, file) in await LoadProgrammaticAsync(null, ct))
        {
            var e = file.Entities.FirstOrDefault(x => string.Equals(x.Id, entityId, StringComparison.Ordinal));
            if (e is not null) return new Located(slug, path, file, e);
        }
        return null;
    }

    private async Task<List<(string Slug, string Path, CanonicalJsonFile File)>> LoadProgrammaticAsync(
        string? bookSlug, CancellationToken ct)
    {
        var result = new List<(string, string, CanonicalJsonFile)>();
        var dir = entityOptions.Value.CanonicalDirectory;
        if (!Directory.Exists(dir)) return result;

        foreach (var path in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
                     .Where(f => !CanonicalSidecarFiles.IsSidecar(f)).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var slug = Path.GetFileNameWithoutExtension(path);
            if (bookSlug is not null && !string.Equals(slug, bookSlug, StringComparison.OrdinalIgnoreCase)) continue;
            CanonicalJsonFile file;
            try { file = await loader.LoadAsync(path, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { continue; }
            if (file.SchemaVersion == ProgrammaticSchema) result.Add((slug, path, file));
        }
        return result;
    }

    private async Task<IngestionRecord?> FindRecordAsync(string slug, CancellationToken ct)
    {
        var all = await tracker.GetAllAsync(1000, 0, ct);
        return all.FirstOrDefault(r => string.Equals(EntityIdSlug.BookSlug(r), slug, StringComparison.OrdinalIgnoreCase));
    }
}
