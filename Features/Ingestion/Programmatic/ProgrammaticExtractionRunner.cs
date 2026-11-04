using System.Diagnostics;
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Ingestion;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

public sealed record ProgrammaticRunResult(
    int Entities, int Accepted, int NeedsReview, IReadOnlyList<string> Orphans, JsonElement Report);

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
    ILogger<ProgrammaticExtractionRunner> logger) : IProgrammaticExtractionRunner
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

        // Point of no return: everything above can fail without touching existing files or points.
        // The new canonical file replaces the old one atomically; then stale sidecars are removed.
        await writer.WriteAsync(Path.Combine(dir, slug + ".json"), file, ct);
        await File.WriteAllTextAsync(Path.Combine(dir, slug + ".report.json"), result.Report.GetRawText(), ct);
        foreach (var sidecar in CanonicalSidecarFiles.ReplaceableSidecarPaths(dir, slug))
            File.Delete(sidecar);

        await ingestion.IngestEntitiesAsync(bookId, ct);

        var accepted = merged.Entities.Count(e => e.Disposition == EntityDisposition.Accepted);
        var needsReview = merged.Entities.Count(e => e.Disposition == EntityDisposition.NeedsReview);
        LogDone(logger, bookId, merged.Entities.Count, accepted, needsReview, merged.OrphanedEntityIds.Count);
        return new ProgrammaticRunResult(merged.Entities.Count, accepted, needsReview, merged.OrphanedEntityIds, result.Report);
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
        Checks: e.Checks,
        Provenance: e.Provenance);

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookId}: extractor job {JobId} submitted")]
    private static partial void LogSubmitted(ILogger logger, int bookId, string jobId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Book {BookId}: programmatic extraction done — {Entities} entities ({Accepted} accepted, {NeedsReview} needs review, {Orphans} orphaned overrides)")]
    private static partial void LogDone(ILogger logger, int bookId, int entities, int accepted, int needsReview, int orphans);
}
