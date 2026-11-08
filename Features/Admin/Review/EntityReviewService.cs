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
    ReviewReindexQueue reindexQueue,
    IBookExtractorClient extractor,
    IIngestionTracker tracker,
    IOptions<EntityIngestionOptions> entityOptions,
    IOptions<IngestionOptions> ingestionOptions,
    CanonicalBookLocks locks)
{
    private const string ProgrammaticSchema = "2";

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


    private Dictionary<EntityType, Dictionary<string, string>>? _examples;

    /// <summary>
    /// For each type, one example value per field, taken from accepted entities of that type in any programmatic
    /// book. Read once per page session (every book file is parsed), so the caller loads it off the critical path.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetFieldExamplesAsync(EntityType type, CancellationToken ct)
    {
        _examples ??= ExamplesByType(await LoadProgrammaticAsync(null, ct));
        return _examples.TryGetValue(type, out var byField) ? byField : new Dictionary<string, string>();
    }

    internal static Dictionary<EntityType, Dictionary<string, string>> ExamplesByType(
        IEnumerable<(string Slug, string Path, CanonicalJsonFile File)> books)
    {
        var result = new Dictionary<EntityType, Dictionary<string, string>>();
        foreach (var (_, _, file) in books)
        {
            foreach (var e in file.Entities)
            {
                if (e.Disposition != EntityDisposition.Accepted || e.NeedsReview) continue;
                if (e.Fields.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                if (!result.TryGetValue(e.Type, out var byField)) result[e.Type] = byField = new();
                foreach (var p in e.Fields.EnumerateObject())
                {
                    if (byField.ContainsKey(p.Name) || IsEmpty(p.Value)) continue;
                    byField[p.Name] = FieldHints.Example(p.Value);
                }
            }
        }
        return result;
    }

    private static bool IsEmpty(System.Text.Json.JsonElement v) => v.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => true,
        System.Text.Json.JsonValueKind.Array => v.GetArrayLength() == 0,
        System.Text.Json.JsonValueKind.Object => !v.EnumerateObject().Any(),
        System.Text.Json.JsonValueKind.String => v.GetString()!.Length == 0,
        _ => false,
    };

    public Task<EntityEnvelope> OverrideAsync(
        string entityId, string field, JsonElement value, string reason, CancellationToken ct) =>
        OverrideManyAsync(entityId, [(field, value)], reason, ct);

    /// <summary>
    /// Applies several field overrides as one all-or-nothing write: every change is validated first, and nothing
    /// is saved if any is invalid. One reindex follows.
    /// </summary>
    public async Task<EntityEnvelope> OverrideManyAsync(
        string entityId, IReadOnlyList<(string Field, JsonElement Value)> changes, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        if (changes.Count == 0) throw new ArgumentException("No changes given.", nameof(changes));
        var cloned = changes.Select(c => (c.Field, Value: c.Value.Clone())).ToList();
        return await MutateAsync(entityId, (e, o) =>
        {
            var updated = e;
            var overrides = o.Overrides.ToList();
            foreach (var (field, value) in cloned)
            {
                if (!OverrideMerger.ValidatePath(updated, field))
                    throw new ArgumentException($"Invalid field path '{field}'.", nameof(changes));
                var exists = OverrideMerger.TryGetValue(updated, field, out var current);
                // JSON null is always allowed: it clears a wrong value without inventing a replacement.
                if (field.StartsWith("fields.", StringComparison.Ordinal) && exists
                    && current.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Null
                    && !SameKind(current.ValueKind, value.ValueKind))
                    throw new ArgumentException(
                        $"'{field}' holds a {KindName(current.ValueKind)}; the new value is a {KindName(value.ValueKind)}.", nameof(changes));

                // The baseline is the extractor's value: the previous override's baseline (rebased to the new
                // extraction if it drifted), or, for a first override, what the entity holds right now.
                var prior = overrides.LastOrDefault(x => x.EntityId == entityId && x.Field == field);
                JsonElement? baseline;
                bool absent;
                if (prior is null)
                {
                    baseline = exists ? current : null;
                    absent = !exists;
                }
                else if (OverrideMerger.DriftNow(e, prior) is { } now)
                {
                    (baseline, absent) = (now, false);
                }
                else
                {
                    (baseline, absent) = (prior.Extracted, prior.ExtractedAbsent);
                }

                updated = OverrideMerger.TrySet(updated, field, value, out var why)
                          ?? throw new ArgumentException($"Cannot apply override at '{field}': {why}", nameof(changes));
                updated = StripOverrideChecks(updated, field);
                overrides.RemoveAll(x => x.EntityId == entityId && x.Field == field);
                overrides.Add(new FieldOverride(entityId, field, value, baseline, reason.Trim(), DateTimeOffset.UtcNow, absent));
            }
            var next = new EntityOverrides(overrides, o.Approvals);
            return (OverrideMerger.SetDisposition(updated, OverrideMerger.ApprovalFor(next, entityId)), next);
        }, ct);
    }

    private static bool SameKind(JsonValueKind a, JsonValueKind b) =>
        a == b || (a is JsonValueKind.True or JsonValueKind.False && b is JsonValueKind.True or JsonValueKind.False);

    private static string KindName(JsonValueKind k) => k switch
    {
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => k.ToString().ToLowerInvariant(),
    };

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
                    rebased.Add(ov with { Extracted = now, ExtractedAbsent = false });
                    updated = StripOverrideChecks(updated, ov.Field, unapplied: false);
                }
                else
                {
                    rebased.Add(ov);
                }
            }

            // The approval waives exactly the checks failing now (drift/unapplied are never waivable), so a later
            // run that fails a different check sends the entity back to review.
            var approval = new Approval(entityId, reason.Trim(), DateTimeOffset.UtcNow,
                OverrideMerger.WaivableFailedChecks(updated));
            var approvals = o.Approvals.Where(a => a.EntityId != entityId).Append(approval).ToList();
            return (OverrideMerger.SetDisposition(updated, approval), new EntityOverrides(rebased, approvals));
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
            if (!unapplied)
            {
                var drift = OverrideMerger.DriftNow(e, ov);
                if ((drift ?? ov.Extracted) is { } restore)
                    updated = OverrideMerger.TrySet(e, field, restore, out _) ?? e;
                else if (ov.ExtractedAbsent)
                    updated = OverrideMerger.TryRemoveKey(e, field) ?? e;
                else
                {
                    // No stored baseline and not absent: the extractor's value was JSON null
                    // (it round-trips through the overrides file as a plain null).
                    using var nullDoc = JsonDocument.Parse("null");
                    updated = OverrideMerger.TrySet(e, field, nullDoc.RootElement, out _) ?? e;
                }
            }
            updated = StripOverrideChecks(updated, field);

            var next = new EntityOverrides(
                o.Overrides.Where(x => !(x.EntityId == entityId && x.Field == field)).ToList(), o.Approvals);
            return (OverrideMerger.SetDisposition(updated, OverrideMerger.ApprovalFor(next, entityId)), next);
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

        var record = await FindRecordAsync(loc.Slug, loc.File.Book.FileHash, ct)
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
        // Shared with the programmatic extraction run, so a save never interleaves with a file swap + ingest.
        using (await locks.AcquireAsync(first.Slug, ct))
        {
            // Re-read under the lock so concurrent saves on the same book never lose each other's changes.
            var loc = await LocateAsync(entityId, ct) ?? throw new KeyNotFoundException($"No programmatic entity '{entityId}'.");
            var record = await FindRecordAsync(loc.Slug, loc.File.Book.FileHash, ct)
                         ?? throw new InvalidOperationException($"No ingestion record for book '{loc.Slug}'.");

            var overrides = await overridesStore.LoadAsync(loc.Slug, ct);
            var (entity, nextOverrides) = change(loc.Entity, overrides);

            await overridesStore.SaveAsync(loc.Slug, nextOverrides, ct);
            var entities = loc.File.Entities.Select(x => x.Id == entityId ? entity : x).ToList();
            await writer.WriteAsync(loc.Path, loc.File with { Entities = entities }, ct);
            // Re-index in the background: the review click returns as soon as the file is written.
            reindexQueue.Enqueue(new ReviewReindexJob(loc.Slug, record.Id, entityId));
            return entity;
        }
    }

    private async Task<Located?> LocateAsync(string entityId, CancellationToken ct)
    {
        // Ids are "<book-slug>.<type>.<name>" and the book file is "<book-slug>.json", so load just
        // that one file. Parsing all 16 books on every open/save/approve made each action take ~10 s.
        var dot = entityId.IndexOf('.');
        if (dot > 0)
        {
            foreach (var (slug, path, file) in await LoadProgrammaticAsync(entityId[..dot], ct))
            {
                var hit = file.Entities.FirstOrDefault(x => string.Equals(x.Id, entityId, StringComparison.Ordinal));
                if (hit is not null) return new Located(slug, path, file, hit);
            }
        }
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

    /// <summary>
    /// The ingestion record behind a canonical file: matched by the file's <c>book.fileHash</c> (several records can
    /// share a slug, e.g. duplicate PDF uploads); falls back to the slug only when exactly one record has it.
    /// </summary>
    private async Task<IngestionRecord?> FindRecordAsync(string slug, string? fileHash, CancellationToken ct)
    {
        var all = await tracker.GetAllAsync(1000, 0, ct);
        var bySlug = all.Where(r => string.Equals(EntityIdSlug.BookSlug(r), slug, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrEmpty(fileHash)
            && bySlug.FirstOrDefault(r => string.Equals(r.FileHash, fileHash, StringComparison.OrdinalIgnoreCase)) is { } exact)
            return exact;
        return bySlug.Count == 1 ? bySlug[0] : null;
    }
}
