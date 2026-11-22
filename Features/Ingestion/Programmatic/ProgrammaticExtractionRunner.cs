using System.Diagnostics;
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Ingestion;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

public sealed record ProgrammaticRunResult(
    int Entities, int Accepted, int NeedsReview, IReadOnlyList<string> Orphans, JsonElement Report,
    int Retired, IReadOnlyList<string> Moved);

/// <summary>A failure after the point of no return: existing files or points were already (partly) replaced.</summary>
public abstract class PostSwapException(string message, Exception inner) : Exception(message, inner);

/// <summary>Writing the canonical file or deleting stale sidecars failed after the report was already replaced.</summary>
public sealed class PartialSwapException(int bookId, Exception inner) : PostSwapException(
    $"Book {bookId}: replacing the canonical files failed partway (the report may be new while the canonical file " +
    $"and/or old sidecars may be unchanged); re-run POST /admin/books/{bookId}/extract-programmatic to complete it.", inner);

/// <summary>Ingestion failed after the new canonical file was swapped in; the file is in place but dnd_entities is stale.</summary>
public sealed class PostSwapIngestionException(int bookId, Exception inner) : PostSwapException(
    $"Book {bookId}: the new canonical file is in place but ingestion into dnd_entities failed; " +
    $"run POST /admin/books/{bookId}/ingest-entities to retry.", inner);

public interface IProgrammaticExtractionRunner
{
    Task<ProgrammaticRunResult> RunAsync(int bookId, CancellationToken ct);
}

/// <summary>
/// Drives the Python extractor for one book and replaces the book's canonical data with its output.
/// Nothing on disk or in Qdrant changes until the extractor has produced a complete, merged result.
/// </summary>
public sealed partial class ProgrammaticExtractionRunner(
    IIngestionTracker tracker,
    IBookExtractorClient client,
    OverridesStore overridesStore,
    IEntityIngestionOrchestrator ingestion,
    BookSourceRegistry registry,
    IOptions<EntityIngestionOptions> entityOptions,
    IOptions<IngestionOptions> ingestionOptions,
    IOptions<BookExtractorOptions> extractorOptions,
    CanonicalJsonWriter writer,
    CanonicalJsonLoader loader,
    IEntityVectorStore entityStore,
    CanonicalBookLocks locks,
    ILogger<ProgrammaticExtractionRunner> logger,
    Action<string>? deleteFile = null) : IProgrammaticExtractionRunner
{
    public async Task<ProgrammaticRunResult> RunAsync(int bookId, CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(bookId, ct)
                     ?? throw new InvalidOperationException($"No ingestion record {bookId}");
        var slug = EntityIdSlug.BookSlug(record);
        var dir = entityOptions.Value.CanonicalDirectory;
        var opts = extractorOptions.Value;

        var pdfPath = Path.GetRelativePath(ingestionOptions.Value.BooksPath, record.FilePath);
        var jobId = await client.SubmitAsync(pdfPath, slug, ct);
        LogSubmitted(logger, bookId, jobId);

        await WaitForJobAsync(jobId, opts, ct);
        var result = await client.GetResultAsync(jobId, ct);

        var (sourceBook, edition) = BookMetadataDeriver.Derive(record, registry);
        var extracted = result.Entities.Select(e => ToEnvelope(e, sourceBook, edition)).ToList();

        MergeResult merged;
        MigrationResult migration;
        int retired;
        // Overrides are read, migrated, merged, reduced and saved inside the lock, so an admin save made on the
        // review page while the extractor ran is neither lost nor overwritten. The swap + ingest is one write on
        // the book: the review page's saves wait for it (and vice versa).
        using (await locks.AcquireAsync(slug, ct))
        {
            var previous = await LoadPreviousEntitiesAsync(Path.Combine(dir, slug + ".json"), ct);
            var loaded = await overridesStore.LoadAsync(slug, ct);
            migration = OverrideMigration.Migrate(loaded, previous, extracted);
            merged = OverrideMerger.Apply(extracted, migration.Overrides);
            var reduced = migration.Overrides with
            {
                Overrides = migration.Overrides.Overrides
                    .Where(o => !merged.Retired.Any(r => r.EntityId == o.EntityId && r.Field == o.Field))
                    .ToList(),
            };
            retired = migration.Overrides.Overrides.Count - reduced.Overrides.Count;
            // Written only after the canonical file is replaced, so a failed swap leaves the overrides file as it was.
            var reducedToSave = retired > 0 || migration.Moved.Count > 0 ? reduced : null;

            var file = new CanonicalJsonFile(
                SchemaVersion: CanonicalJsonSchema.CurrentVersion,
                Book: new CanonicalBookMetadata(sourceBook, edition, record.FileHash, record.DisplayName),
                Entities: merged.Entities);
            await SwapAndIngestAsync(record, bookId, slug, dir, file, result, sourceBook, reducedToSave, ct);
        }

        var moved = migration.Moved.Select(m => $"{m.From} -> {m.To}").ToList();
        var accepted = merged.Entities.Count(e => e.Disposition == EntityDisposition.Accepted);
        var needsReview = merged.Entities.Count(e => e.Disposition == EntityDisposition.NeedsReview);
        LogDone(logger, bookId, merged.Entities.Count, accepted, needsReview, migration.Orphaned.Count, retired, moved.Count);
        if (moved.Count > 0)
            LogMoved(logger, bookId, string.Join("; ", moved));
        if (migration.Orphaned.Count > 0)
            LogOrphaned(logger, bookId, string.Join(", ", migration.Orphaned));
        if (merged.Retired.Count > 0)
            LogRetired(logger, bookId, string.Join("; ", merged.Retired.Select(r => $"{r.EntityId}.{r.Field}")));
        return new ProgrammaticRunResult(
            merged.Entities.Count, accepted, needsReview, migration.Orphaned, result.Report, retired, moved);
    }

    /// <summary>The canonical file about to be replaced; none when absent or unreadable (no rename candidates then).</summary>
    private async Task<IReadOnlyList<EntityEnvelope>> LoadPreviousEntitiesAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return [];
        try
        {
            return (await loader.LoadAsync(path, ct)).Entities;
        }
        catch (Exception ex) when (ex is JsonException or CanonicalJsonSchemaException)
        {
            return [];
        }
    }

    private async Task SwapAndIngestAsync(
        DndMcpAICsharpFun.Domain.IngestionRecord record, int bookId, string slug, string dir, CanonicalJsonFile file,
        ExtractorResult result, string sourceBook, EntityOverrides? reducedOverrides, CancellationToken ct)
    {
        // Point of no return: everything above can fail without touching existing files or points.
        // Report first (tmp + move), then the canonical file (atomic overwrite), then stale sidecars,
        // so a failed write can never leave a new canonical file beside old sidecars.
        var reportPath = Path.Combine(dir, slug + ".report.json");
        var reportTmp = reportPath + ".tmp";
        try
        {
            // Still before any replacement: a failure here leaves existing files untouched.
            await File.WriteAllTextAsync(reportTmp, result.Report.GetRawText(), ct);
            File.Move(reportTmp, reportPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(reportTmp); } catch { /* best effort */ }
            throw;
        }

        try
        {
            await writer.WriteAsync(Path.Combine(dir, slug + ".json"), file, ct);
            if (reducedOverrides is not null)
                await overridesStore.SaveAsync(slug, reducedOverrides, ct);
            foreach (var sidecar in CanonicalSidecarFiles.ReplaceableSidecarPaths(dir, slug))
                (deleteFile ?? File.Delete)(sidecar);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPartialSwap(logger, ex, bookId);
            throw new PartialSwapException(bookId, ex);
        }

        // No rollback: the old data is known-wrong. If ingestion fails the new canonical file stays in place
        // and the admin re-runs ingest-entities to bring dnd_entities in line.
        try
        {
            await ingestion.IngestEntitiesAsync(bookId, ct);
            // Prior 5etools imports wrote points for this book under "5etools:<KEY>"; flagged programmatic entities
            // are deliberately not indexed, so those stale points would otherwise keep answering for them.
            if (!string.IsNullOrWhiteSpace(record.FivetoolsSourceKey))
                await FivetoolsImportedPoints.PurgeAsync(entityStore, record.FivetoolsSourceKey, ct);
            // Ingestion only deletes by this record's file hash, so points an earlier LLM extraction wrote for the
            // book (empty or different hash) would survive. Points carry source_book = FivetoolsSourceKey ?? the
            // derived source book, so purge everything for that key that this run did not write.
            await entityStore.DeleteStaleForBookAsync(
                string.IsNullOrWhiteSpace(record.FivetoolsSourceKey) ? sourceBook : record.FivetoolsSourceKey,
                record.FileHash, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogIngestAfterSwapFailed(logger, ex, bookId);
            throw new PostSwapIngestionException(bookId, ex);
        }
    }

    private async Task WaitForJobAsync(string jobId, BookExtractorOptions opts, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var status = await client.GetStatusAsync(jobId, ct);
            switch (status.Status)
            {
                case "done":
                    return;
                case "failed":
                    throw new InvalidOperationException(
                        $"Extractor job {jobId} failed: {status.Error ?? "unknown error"}");
            }

            if (sw.Elapsed >= opts.Timeout)
                throw new TimeoutException(
                    $"Extractor job {jobId} did not finish within {opts.Timeout} ({status.PagesDone}/{status.PagesTotal} pages).");
            await Task.Delay(opts.PollInterval, ct);
        }
    }

    private static EntityEnvelope ToEnvelope(ExtractedEntity e, string sourceBook, string edition) => new(
        Id: e.Id,
        Type: e.Type,
        Name: e.Name,
        SourceBook: sourceBook,
        Edition: edition,
        Page: e.Page,
        FirstAppearedIn: new FirstAppearance(sourceBook, edition, e.Page),
        RevisedIn: [],
        SettingTags: [],
        CanonicalText: e.CanonicalText,
        Fields: e.Fields,
        DataSource: DataSources.Programmatic,
        Authority: "book",
        // Fail closed: an entity the extractor did not verify at all must never be auto-accepted.
        Checks: e.Checks is { Count: > 0 } ? e.Checks : [new EntityCheck(ChecksMissing, false, "extractor returned no checks")],
        Provenance: e.Provenance,
        Notes: e.Notes is { Count: > 0 } ? e.Notes : null);

    /// <summary>Failed check added when the extractor returned an entity with no checks.</summary>
    public const string ChecksMissing = "checks-missing";

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookId}: extractor job {JobId} submitted")]
    private static partial void LogSubmitted(ILogger logger, int bookId, string jobId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Book {BookId}: replacing the canonical files failed partway; existing files are partly replaced. Re-run POST /admin/books/{BookId}/extract-programmatic")]
    private static partial void LogPartialSwap(ILogger logger, Exception ex, int bookId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Book {BookId}: the new canonical file is in place, but ingestion failed; dnd_entities is stale. Run POST /admin/books/{BookId}/ingest-entities to retry")]
    private static partial void LogIngestAfterSwapFailed(ILogger logger, Exception ex, int bookId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Book {BookId}: programmatic extraction done — {Entities} entities ({Accepted} accepted, {NeedsReview} needs review, {Orphans} orphaned overrides, {Retired} retired, {Moved} moved)")]
    private static partial void LogDone(
        ILogger logger, int bookId, int entities, int accepted, int needsReview, int orphans, int retired, int moved);

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookId}: overrides moved to renamed entities (from -> to): {Moved}")]
    private static partial void LogMoved(ILogger logger, int bookId, string moved);

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookId}: orphaned override entity ids: {Orphans}")]
    private static partial void LogOrphaned(ILogger logger, int bookId, string orphans);

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookId}: retired overrides (entity.field): {Retired}")]
    private static partial void LogRetired(ILogger logger, int bookId, string retired);
}
