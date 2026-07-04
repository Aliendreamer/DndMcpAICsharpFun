using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Resolution;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;

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
        group.MapPost("/books/{id:int}/backfill-entities", BackfillEntities).DisableAntiforgery();
        group.MapPost("/books/{id:int}/flag-unknown-entities", FlagUnknownEntities).DisableAntiforgery();
        group.MapPost("/books/{id:int}/project-structured", ProjectStructured).DisableAntiforgery();
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

        queue.TryEnqueue(new IngestionWorkItem(workType, id));
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

        queue.TryEnqueue(new IngestionWorkItem(
            IngestionWorkType.ExtractEntities, id,
            Force: forceFlag, ErrorsOnly: errorsOnlyFlag));
        return Results.Accepted($"/admin/books/{id}");
    }

    private static IResult? UnsupportedType(string type) => Results.Problem(
        $"Unsupported type '{type}'. Supported: Monster, Spell, MagicItem, God.",
        statusCode: StatusCodes.Status400BadRequest);

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
            return UnsupportedType(type)!;

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

    private static async Task<IResult> BackfillEntities(
        int id,
        [FromQuery] string type,
        [FromServices] IIngestionTracker tracker,
        [FromServices] IReadOnlyDictionary<EntityType, EntityBackfillService> services,
        [FromServices] CanonicalJsonLoader loader,
        [FromServices] CanonicalJsonWriter writer,
        CancellationToken ct)
    {
        if (!TryResolveService(type, services, out var svc))
            return UnsupportedType(type)!;

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

    private static async Task<IResult> FlagUnknownEntities(
        int id,
        [FromQuery] string type,
        [FromServices] IIngestionTracker tracker,
        [FromServices] IReadOnlyDictionary<EntityType, EntityBackfillService> services,
        [FromServices] CanonicalJsonWriter writer,
        CancellationToken ct)
    {
        if (!TryResolveService(type, services, out var svc))
            return UnsupportedType(type)!;

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
