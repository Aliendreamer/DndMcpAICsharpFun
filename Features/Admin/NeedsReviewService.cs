using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record NeedsReviewItem(
    string Id,
    string Name,
    string Book,
    EntityType Type,
    int? Page,
    string Reason);

public sealed record NeedsReviewListResponse(
    IReadOnlyList<NeedsReviewItem> Items,
    int Total);

public sealed record ResolveRequest(
    string Action,          // "accept" | "edit"
    string? Name = null,
    JsonElement? Fields = null);

public sealed record BulkAcceptRequest(
    string? Book = null,
    string? Reason = null);

public sealed record BulkAcceptResponse(int Cleared);

// ── Service ───────────────────────────────────────────────────────────────────

public sealed class NeedsReviewService(
    CanonicalJsonLoader loader,
    CanonicalJsonWriter writer,
    IEntityIngestionOrchestrator orchestrator,
    IIngestionTracker tracker,
    IOptions<EntityExtractionOptions> options)
{
    private readonly EntityExtractionOptions _opts = options.Value;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string DeriveReason(string name) =>
        ExtractionNeedsReview.HasOcrArtifacts(name) ? "ocr-artifact" : "low-confidence";

    private string[] GetCanonicalFiles()
    {
        if (!Directory.Exists(_opts.CanonicalDirectory))
            return [];

        return Directory.GetFiles(_opts.CanonicalDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Where(f => !CanonicalSidecarFiles.IsSidecar(f))
            .OrderBy(f => f)
            .ToArray();
    }

    private static string BookSlugFromPath(string path) =>
        Path.GetFileNameWithoutExtension(path);

    // ── List ──────────────────────────────────────────────────────────────────

    public async Task<NeedsReviewListResponse> ListAsync(
        string? bookFilter,
        string? reasonFilter,
        int offset,
        int limit,
        CancellationToken ct)
    {
        var allItems = new List<NeedsReviewItem>();

        foreach (var path in GetCanonicalFiles())
        {
            ct.ThrowIfCancellationRequested();
            var slug = BookSlugFromPath(path);

            // Quick book-slug filter before loading the file.
            if (bookFilter is not null &&
                !string.Equals(slug, bookFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            CanonicalJsonFile file;
            try { file = await loader.LoadAsync(path, ct); }
            catch { continue; }

            foreach (var e in file.Entities)
            {
                if (!e.NeedsReview) continue;
                var reason = DeriveReason(e.Name);
                if (reasonFilter is not null &&
                    !string.Equals(reason, reasonFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                allItems.Add(new NeedsReviewItem(e.Id, e.Name, slug, e.Type, e.Page, reason));
            }
        }

        var total = allItems.Count;
        var paged = allItems.Skip(offset).Take(limit).ToList();
        return new NeedsReviewListResponse(paged, total);
    }

    // ── Get one ───────────────────────────────────────────────────────────────

    public async Task<EntityEnvelope?> GetAsync(string entityId, CancellationToken ct)
    {
        foreach (var path in GetCanonicalFiles())
        {
            ct.ThrowIfCancellationRequested();
            CanonicalJsonFile file;
            try { file = await loader.LoadAsync(path, ct); }
            catch { continue; }

            var found = file.Entities.FirstOrDefault(e =>
                string.Equals(e.Id, entityId, StringComparison.Ordinal));
            if (found is not null) return found;
        }
        return null;
    }

    // ── Resolve (accept / edit) ───────────────────────────────────────────────

    /// <returns>
    ///   <c>true</c> on success (including idempotent re-resolve of already-cleared entity);
    ///   <c>false</c> when the id is not found in any canonical file.
    /// </returns>
    public async Task<bool> ResolveAsync(
        string entityId,
        string action,
        string? name,
        JsonElement? fields,
        CancellationToken ct)
    {
        if (!string.Equals(action, "accept", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(action, "edit",   StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown action '{action}'. Expected 'accept' or 'edit'.", nameof(action));

        // Only set name/fields when the action is "edit".
        var applyName   = string.Equals(action, "edit", StringComparison.OrdinalIgnoreCase) ? name   : null;
        var applyFields = string.Equals(action, "edit", StringComparison.OrdinalIgnoreCase) ? fields : null;

        foreach (var path in GetCanonicalFiles())
        {
            ct.ThrowIfCancellationRequested();
            CanonicalJsonFile file;
            try { file = await loader.LoadAsync(path, ct); }
            catch { continue; }

            var entity = file.Entities.FirstOrDefault(e =>
                string.Equals(e.Id, entityId, StringComparison.Ordinal));
            if (entity is null) continue;

            // Found the entity — patch and reindex.
            var patched = await writer.PatchEntityAsync(
                path, entityId, applyName, applyFields, loader, ct);
            if (!patched) return false; // should not happen, but guard

            // Find the owning book record for the reindex call.
            var bookId = await FindBookIdForSlugAsync(BookSlugFromPath(path), ct);
            if (bookId.HasValue)
                await orchestrator.ReindexEntityAsync(bookId.Value, entityId, ct);

            return true;
        }

        return false; // entity not found
    }

    // ── Bulk accept ───────────────────────────────────────────────────────────

    public async Task<int> BulkAcceptAsync(
        string? bookFilter,
        string? reasonFilter,
        CancellationToken ct)
    {
        int cleared = 0;

        foreach (var path in GetCanonicalFiles())
        {
            ct.ThrowIfCancellationRequested();
            var slug = BookSlugFromPath(path);

            if (bookFilter is not null &&
                !string.Equals(slug, bookFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            CanonicalJsonFile file;
            try { file = await loader.LoadAsync(path, ct); }
            catch { continue; }

            var toAccept = file.Entities
                .Where(e => e.NeedsReview)
                .Where(e => reasonFilter is null ||
                    string.Equals(DeriveReason(e.Name), reasonFilter, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Id)
                .ToList();

            if (toAccept.Count == 0) continue;

            var bookId = await FindBookIdForSlugAsync(slug, ct);

            foreach (var id in toAccept)
            {
                var patched = await writer.PatchEntityAsync(path, id, null, null, loader, ct);
                if (!patched) continue;

                if (bookId.HasValue)
                    await orchestrator.ReindexEntityAsync(bookId.Value, id, ct);

                cleared++;
            }
        }

        return cleared;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<int?> FindBookIdForSlugAsync(string slug, CancellationToken ct)
    {
        var all = await tracker.GetAllAsync(1000, 0, ct);
        foreach (var rec in all)
        {
            var recSlug = EntityIdSlug.BookSlug(rec);
            if (string.Equals(recSlug, slug, StringComparison.OrdinalIgnoreCase))
                return rec.Id;
        }
        return null;
    }
}
