using System.Diagnostics;
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Ingestion;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

public sealed record ProgrammaticRunResult(
    int Entities, int Accepted, int NeedsReview, IReadOnlyList<string> Orphans, JsonElement Report);

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
        var overrides = await overridesStore.LoadAsync(slug, ct);
        var merged = OverrideMerger.Apply(extracted, overrides);

        var file = new CanonicalJsonFile(
            SchemaVersion: CanonicalJsonSchema.CurrentVersion,
            Book: new CanonicalBookMetadata(sourceBook, edition, record.FileHash, record.DisplayName),
            Entities: merged.Entities);

        // The swap + ingest is one write on the book: the review page's saves wait for it (and vice versa).
        using (await locks.AcquireAsync(slug, ct))
            await SwapAndIngestAsync(record, bookId, slug, dir, file, result, sourceBook, ct);

        var accepted = merged.Entities.Count(e => e.Disposition == EntityDisposition.Accepted);
        var needsReview = merged.Entities.Count(e => e.Disposition == EntityDisposition.NeedsReview);
        LogDone(logger, bookId, merged.Entities.Count, accepted, needsReview, merged.OrphanedEntityIds.Count);
        return new ProgrammaticRunResult(merged.Entities.Count, accepted, needsReview, merged.OrphanedEntityIds, result.Report);
    }

    private async Task SwapAndIngestAsync(
        DndMcpAICsharpFun.Domain.IngestionRecord record, int bookId, string slug, string dir, CanonicalJsonFile file,
        ExtractorResult result, string sourceBook, CancellationToken ct)
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
        Provenance: e.Provenance);

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
        Message = "Book {BookId}: programmatic extraction done — {Entities} entities ({Accepted} accepted, {NeedsReview} needs review, {Orphans} orphaned overrides)")]
    private static partial void LogDone(ILogger logger, int bookId, int entities, int accepted, int needsReview, int orphans);
}
