using System.Diagnostics.CodeAnalysis;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Resolution;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin;

public static partial class BooksAdminEndpoints
{
    private sealed record RegisterBookResponse(
        IngestionRecord Record,
        IReadOnlyList<string> SuggestedSources);

    public static RouteGroupBuilder MapBooksAdmin(this RouteGroupBuilder group)
    {
        group.MapPost("/books/register", RegisterBook).DisableAntiforgery();
        group.MapGet("/books", GetAllBooks);
        group.MapDelete("/books/{id:int}", DeleteBook);
        group.MapPost("/books/{id:int}/ingest-blocks", IngestBlocks).DisableAntiforgery();
        group.MapPost("/books/{id:int}/ingest-entities", IngestEntities).DisableAntiforgery();
        group.MapPost("/books/{id:int}/extract-entities", ExtractEntities).DisableAntiforgery();
        group.MapGet("/books/{id:int}/entity-recall", EntityRecall);
        group.MapGet("/books/{id:int}/coverage", GetCoverage);
        group.MapPost("/books/{id:int}/backfill-entities", BackfillEntities).DisableAntiforgery();
        group.MapPost("/books/{id:int}/fill-fields", FillFields).DisableAntiforgery();
        group.MapPost("/books/{id:int}/flag-unknown-entities", FlagUnknownEntities).DisableAntiforgery();
        group.MapPost("/books/{id:int}/project-structured", ProjectStructured).DisableAntiforgery();
        group.MapPost("/books/{id:int}/reground-entities", Reground).DisableAntiforgery();
        group.MapPost("/books/reconcile", Reconcile).DisableAntiforgery();
        return group;
    }

    private static Task<IResult> IngestBlocks(
        int id,
        IIngestionTracker tracker,
        IIngestionQueue queue,
        CancellationToken ct)
        => EnqueueOrFailAsync(id, IngestionWorkType.IngestBlocks, tracker, queue, ct);

    private static Task<IResult> IngestEntities(
        int id,
        IIngestionTracker tracker,
        IIngestionQueue queue,
        CancellationToken ct)
        => EnqueueOrFailAsync(id, IngestionWorkType.IngestEntities, tracker, queue, ct);

    private static async Task<IResult> ProjectStructured(
        int id,
        [FromServices] IIngestionTracker tracker,
        [FromServices] CanonicalJsonLoader loader,
        [FromServices] StructuredFactProjector projector,
        [FromServices] IOptions<EntityExtractionOptions> opts,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        var slug = EntityIdSlug.BookSlug(record);
        var path = Path.Combine(opts.Value.CanonicalDirectory, slug + ".json");

        if (!File.Exists(path))
            return Results.NotFound($"No canonical file found at {path}");

        var file = await loader.LoadAsync(path, ct);
        var (t, r, c) = await projector.ProjectAsync(file, ct);
        return Results.Ok(new { tables = t, rows = r, choiceSets = c });
    }

    /// <summary>
    /// Shared handler for book-level enqueue endpoints (IngestBlocks, IngestEntities).
    /// Returns NotFound, Conflict, or Accepted after looking up the book and checking its status.
    /// </summary>
    private static async Task<IResult> EnqueueOrFailAsync(
        int id,
        IngestionWorkType workType,
        IIngestionTracker tracker,
        IIngestionQueue queue,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        if (record.Status is IngestionStatus.Processing
            or IngestionStatus.EntitiesExtracting
            or IngestionStatus.EntitiesIngesting)
            return Results.Conflict("Book is currently processing.");

        if (!queue.TryEnqueue(new IngestionWorkItem(workType, id)))
            return Results.Conflict("Book is already queued for processing.");

        return Results.Accepted($"/admin/books/{id}");
    }

    private static async Task<IResult> ExtractEntities(
        int id,
        bool? force,
        bool? errorsOnly,
        IIngestionTracker tracker,
        IIngestionQueue queue,
        IOptions<EntityExtractionOptions> opts,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        var forceFlag = force ?? false;
        var errorsOnlyFlag = errorsOnly ?? false;

        // A stuck EntitiesExtracting/EntitiesIngesting status left by an interrupted run can be
        // overridden with ?force=true; an in-flight block ingestion (Processing) is never overridden.
        var stuckEntityStatus = record.Status is IngestionStatus.EntitiesExtracting
            or IngestionStatus.EntitiesIngesting;
        if (record.Status == IngestionStatus.Processing || (stuckEntityStatus && !forceFlag))
            return Results.Conflict("Book is currently processing.");

        if (forceFlag && errorsOnlyFlag)
            return Results.Problem(
                "?force and ?errorsOnly are mutually exclusive.",
                statusCode: StatusCodes.Status400BadRequest);

        if (errorsOnlyFlag)
        {
            var bookSlug = CanonicalSlugFor(record);
            var canonicalPath = Path.Combine(opts.Value.CanonicalDirectory, $"{bookSlug}.json");
            if (!File.Exists(canonicalPath))
                return Results.Conflict($"No canonical file found for {bookSlug}; run full extraction first.");
        }

        if (!errorsOnlyFlag)
        {
            var bookSlug = CanonicalSlugFor(record);
            var canonicalPath = Path.Combine(opts.Value.CanonicalDirectory, $"{bookSlug}.json");
            if (File.Exists(canonicalPath) && !forceFlag)
                return Results.Conflict($"Canonical file already exists at {canonicalPath}. Use ?force=true to overwrite.");
        }

        if (!queue.TryEnqueue(new IngestionWorkItem(
                IngestionWorkType.ExtractEntities, id,
                Force: forceFlag, ErrorsOnly: errorsOnlyFlag)))
            return Results.Conflict("Book is already queued for processing.");

        return Results.Accepted($"/admin/books/{id}");
    }

    private static async Task<IResult> Reground(int id, RegroundService svc, CancellationToken ct, bool judge = false) =>
        Results.Ok(await svc.RegroundAsync(id, judge, ct));

    private static async Task<IResult> Reconcile(RegistryReconcileService svc, CancellationToken ct) =>
        Results.Ok(new { created = await svc.ReconcileAsync(ct) });

    /// <summary>
    /// M-2 (whole-branch review): enumerates the ACTUALLY registered backfill provider types
    /// (the <paramref name="services"/> dictionary keys) instead of a hand-maintained literal
    /// list — the old "Supported: Monster, Spell, MagicItem, God." message went stale the moment
    /// the 9 catalog/base-item providers were added.
    /// </summary>
    private static IResult UnsupportedType(string type, IReadOnlyDictionary<EntityType, EntityBackfillService> services)
    {
        var supported = string.Join(", ", services.Keys.Select(t => t.ToString()).OrderBy(t => t, StringComparer.Ordinal));
        return Results.Problem(
            $"Unsupported type '{type}'. Supported: {supported}.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static bool TryResolveService(
        string type,
        IReadOnlyDictionary<EntityType, EntityBackfillService> services,
        [NotNullWhen(true)] out EntityBackfillService? service)
    {
        if (Enum.TryParse<EntityType>(type, ignoreCase: true, out var et) && services.TryGetValue(et, out service))
            return true;

        service = null;
        return false;
    }

    private static async Task<IResult> EntityRecall(
        int id,
        [FromQuery] string type,
        [FromServices] IIngestionTracker tracker,
        [FromServices] IReadOnlyDictionary<EntityType, EntityBackfillService> services,
        CancellationToken ct)
    {
        if (!TryResolveService(type, services, out var svc))
            return UnsupportedType(type, services);

        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        var result = await svc.ComputeAsync(record, ct);

        return Results.Ok(new
        {
            hasSourceKey = result.HasSourceKey,
            canonicalPath = result.CanonicalPath,
            present = result.AlreadyPresent,
            grounded = result.GroundedCount,
            backfilled = result.BackfilledCount,
            missing = result.Missing,
            extra = result.Extra,
            extraOtherSource = result.ExtraOtherSource,
            extraUnknown = result.ExtraUnknown,
        });
    }

    /// <summary>
    /// Read-only 5etools coverage report for a book: per-type roster/present/missing counts (with
    /// NAMED gaps) plus the unmodeled-content bucket. Never applies a backfill. A book with no
    /// <c>fivetoolsSourceKey</c> returns the empty/no-op <see cref="BookCoverage"/>.
    /// </summary>
    private static async Task<IResult> GetCoverage(
        int id,
        [FromServices] IIngestionTracker tracker,
        [FromServices] FivetoolsCoverageService coverage,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        var result = await coverage.ComputeAsync(record, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> BackfillEntities(
        int id,
        [FromQuery] string type,
        [FromServices] IIngestionTracker tracker,
        [FromServices] IReadOnlyDictionary<EntityType, EntityBackfillService> services,
        [FromServices] CanonicalJsonLoader loader,
        [FromServices] CanonicalJsonWriter writer,
        CancellationToken ct)
    {
        if (string.Equals(type, "all", StringComparison.OrdinalIgnoreCase))
            return await BackfillAllTypes(id, tracker, services, loader, writer, ct);

        if (!TryResolveService(type, services, out var svc))
            return UnsupportedType(type, services);

        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        var result = await svc.ComputeAsync(record, ct);

        if (!result.HasSourceKey)
            return Results.Problem(
                $"Book has no fivetoolsSourceKey; {type} backfill requires an official 5etools source.",
                statusCode: StatusCodes.Status400BadRequest);

        if (result.CanonicalPath is null || !File.Exists(result.CanonicalPath))
            return Results.Conflict($"No canonical file found for book {id}; run extraction first.");

        if (result.ToAppend.Count > 0)
        {
            var file = await loader.LoadAsync(result.CanonicalPath, ct);
            var merged = file.Entities.Concat(result.ToAppend).ToList();
            await writer.WriteAsync(result.CanonicalPath, file with { Entities = merged }, ct);
        }

        return Results.Ok(new
        {
            backfilled = result.ToAppend.Select(e => e.Name).ToList(),
            alreadyPresent = result.AlreadyPresent,
        });
    }

    /// <summary>
    /// Batch mode for <c>?type=all</c>: diffs EVERY registered <see cref="EntityBackfillService"/>
    /// against a SINGLE canonical snapshot (each provider's <see cref="EntityBackfillService.ComputeAsync"/>
    /// only reads; none of them write) and appends all gaps in ONE write. This exists to fix a
    /// lost-update race: calling <c>backfill-entities?type=&lt;T&gt;</c> once per type (13x per book) does
    /// a load→append→write of the WHOLE canonical each time, and rapid-fire writes on the WSL2 bind
    /// mount (plus a read cache) can make a later request's load miss an earlier request's write —
    /// silently dropping that type's appended entities. One write per book eliminates the race.
    /// </summary>
    private static async Task<IResult> BackfillAllTypes(
        int id,
        IIngestionTracker tracker,
        IReadOnlyDictionary<EntityType, EntityBackfillService> services,
        CanonicalJsonLoader loader,
        CanonicalJsonWriter writer,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        var results = new List<EntityBackfillResult>();
        foreach (var svc in services.Values)
        {
            var result = await svc.ComputeAsync(record, ct);

            if (!result.HasSourceKey)
                return Results.Problem(
                    "Book has no fivetoolsSourceKey; all backfill requires an official 5etools source.",
                    statusCode: StatusCodes.Status400BadRequest);

            results.Add(result);
        }

        var canonicalPath = results.Count > 0 ? results[0].CanonicalPath : null;
        if (canonicalPath is null || !File.Exists(canonicalPath))
            return Results.Conflict($"No canonical file found for book {id}; run extraction first.");

        var toAppend = results.SelectMany(r => r.ToAppend).ToList();
        var alreadyPresent = results.Sum(r => r.AlreadyPresent);
        var backfilledByType = results
            .Where(r => r.ToAppend.Count > 0)
            .ToDictionary(
                r => r.ToAppend[0].Type.ToString(),
                r => r.ToAppend.Select(e => e.Name).ToList());

        if (toAppend.Count > 0)
        {
            var file = await loader.LoadAsync(canonicalPath, ct);
            var merged = file.Entities.Concat(toAppend).ToList();

            // Defensive dedup guard: providers are keyed by distinct EntityType so a cross-provider
            // id collision shouldn't occur, but this protects the canonical from ever being written
            // with a duplicate id (CanonicalJsonLoader would throw on load anyway).
            var duplicateIds = merged
                .GroupBy(e => e.Id, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicateIds.Count > 0)
                return Results.Problem(
                    $"Batch backfill would introduce duplicate entity ids: {string.Join(", ", duplicateIds)}.",
                    statusCode: StatusCodes.Status409Conflict);

            await writer.WriteAsync(canonicalPath, file with { Entities = merged }, ct);
        }

        return Results.Ok(new
        {
            backfilled = backfilledByType,
            alreadyPresent,
        });
    }


    private static async Task<IResult> FillFields(
        int id,
        [FromServices] IIngestionTracker tracker,
        [FromServices] EntityFieldFillService fill,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        var result = await fill.FillAsync(record, ct);
        if (result.HasSourceKey && result.CanonicalPath is not null && !File.Exists(result.CanonicalPath))
            return Results.Conflict($"No canonical file found for book {id}; run extraction first.");

        return Results.Ok(new
        {
            hasSourceKey = result.HasSourceKey,
            entitiesTouched = result.EntitiesTouched,
            filledByType = result.FilledByType.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        });
    }

    private static async Task<IResult> FlagUnknownEntities(
        int id,
        [FromQuery] string type,
        [FromServices] IIngestionTracker tracker,
        [FromServices] IReadOnlyDictionary<EntityType, EntityBackfillService> services,
        [FromServices] CanonicalJsonWriter writer,
        CancellationToken ct)
    {
        if (!TryResolveService(type, services, out var svc))
            return UnsupportedType(type, services);

        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        var result = await svc.FlagUnknownAsync(record, writer, ct);

        if (!result.HasSourceKey)
            return Results.Problem(
                $"Book has no fivetoolsSourceKey; {type} flagging requires an official 5etools source.",
                statusCode: StatusCodes.Status400BadRequest);

        if (result.CanonicalPath is null || !File.Exists(result.CanonicalPath))
            return Results.Conflict($"No canonical file found for book {id}; run extraction first.");

        return Results.Ok(new
        {
            flagged = result.Flagged,
            flaggedCount = result.Flagged.Count,
        });
    }

    private static async Task<IResult> RegisterBook(
        HttpContext httpContext,
        BookRegistrationService registration,
        CancellationToken ct)
    {
        var result = await registration.RegisterAsync(
            httpContext.Request.ContentType, httpContext.Request.Body, ct);
        return result switch
        {
            BookRegistrationResult.Success s =>
                Results.Created($"/admin/books/{s.Record.Id}", new RegisterBookResponse(s.Record, s.Suggestions)),
            BookRegistrationResult.Unprocessable u => Results.UnprocessableEntity(u.Message),
            BookRegistrationResult.BadRequest b => Results.Problem(b.Message, statusCode: 400),
            _ => Results.Problem("Unknown registration error.", statusCode: 500),
        };
    }

    private static async Task<IResult> GetAllBooks(
        IIngestionTracker tracker,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        var records = await tracker.GetAllAsync(limit, offset, ct);
        return Results.Ok(records);
    }

    private static async Task<IResult> DeleteBook(
        int id,
        IBookDeletionService deletionService,
        CancellationToken ct)
    {
        var result = await deletionService.DeleteBookAsync(id, ct);
        return result switch
        {
            DeleteBookResult.Deleted => Results.NoContent(),
            DeleteBookResult.NotFound => Results.NotFound(),
            DeleteBookResult.Conflict => Results.Conflict("Book is currently being ingested. Wait for ingestion to complete before deleting."),
            _ => Results.StatusCode(500)
        };
    }

    private static string CanonicalSlugFor(IngestionRecord record) =>
        EntityIdSlug.BookSlug(record);
}

[ExcludeFromCodeCoverage]
public sealed record RegisterBookRequest(
    string SourceName,
    string Version,
    string DisplayName);