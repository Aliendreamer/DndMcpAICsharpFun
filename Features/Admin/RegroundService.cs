using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using Microsoft.Extensions.Options;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Features.Admin;

/// <summary>
/// Per-book backlog pass over NeedsReview entities (Task 7 of the entity-grounding-cascade
/// effort): re-grades each flagged entity with the shared <see cref="IGroundingCascade"/> and
/// applies <see cref="GroundingActionPolicy"/>'s verdict → action mapping. Reuses
/// <see cref="NeedsReviewService"/>'s canonical load/write-back + targeted
/// <see cref="IEntityIngestionOrchestrator.ReindexEntityAsync"/> pattern, and mirrors
/// <see cref="ExtractionCheckpointStore"/>'s crash-resumable checkpoint pattern (atomic tmp-file
/// write, resume by skipping already-processed ids) with a <c>&lt;slug&gt;.reground.progress.json</c>
/// sidecar recording processed entity ids.
/// </summary>
public sealed class RegroundService(
    CanonicalJsonLoader loader,
    CanonicalJsonWriter writer,
    IEntityIngestionOrchestrator orchestrator,
    IIngestionTracker tracker,
    IGroundingCascade cascade,
    IEmbeddingService embeddings,
    IQdrantSearchClient qdrant,
    IOptions<QdrantOptions> qdrantOptions,
    IOptions<GroundingOptions> groundingOptions,
    IOptions<EntityExtractionOptions> options)
{
    // Mirrors EntityExtractionOptions.CheckpointIntervalCandidates in spirit — flush canonical +
    // checkpoint every N processed entities so a crash loses at most this many re-grades of work,
    // never a torn/inconsistent write.
    private const int CheckpointInterval = 50;
    private const int SourceProseHitLimit = 5;

    private static readonly JsonSerializerOptions CheckpointJsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly EntityExtractionOptions _opts = options.Value;

    public async Task<RegroundResult> RegroundAsync(int bookId, bool judge, CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(bookId, ct)
                     ?? throw new InvalidOperationException($"No ingestion record {bookId}");
        var bookSlug = EntityIdSlug.BookSlug(record);
        var path = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Canonical JSON not found for book {bookId} at {path}", path);

        var file = await loader.LoadAsync(path, ct);
        var entities = file.Entities.ToList();

        var flaggedIds = entities.Where(IsFlagged).Select(e => e.Id).ToList();

        var checkpointPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".reground.progress.json");
        var doneIds = await LoadCheckpointAsync(checkpointPath, ct);

        var changedIds = new HashSet<string>(StringComparer.Ordinal);
        var tier2Invoked = 0;
        var processedSinceFlush = 0;

        foreach (var id in flaggedIds)
        {
            ct.ThrowIfCancellationRequested();
            if (doneIds.Contains(id)) continue;

            var idx = entities.FindIndex(e => string.Equals(e.Id, id, StringComparison.Ordinal));
            var entity = entities[idx];

            var sourceProse = await AssembleSourceProseAsync(entity, ct);
            var verdict = await cascade.GradeAsync(entity, sourceProse, judge, ct);
            var action = GroundingActionPolicy.Decide(verdict, entity.Name);

            if (verdict.DecidedByTier == 2) tier2Invoked++;

            switch (action)
            {
                case GroundingAction.Promote:
                    entities[idx] = entity with { Disposition = EntityDisposition.Accepted, NeedsReview = false };
                    changedIds.Add(id);
                    break;
                case GroundingAction.MarkUngrounded:
                    // NeedsReview stays true (per Task 6 semantics: it records "!= Accepted"),
                    // while Disposition carries the more specific Ungrounded verdict.
                    entities[idx] = entity with { Disposition = EntityDisposition.Ungrounded, NeedsReview = true };
                    changedIds.Add(id);
                    break;
                case GroundingAction.LeaveFlagged:
                default:
                    break;
            }

            doneIds.Add(id);
            processedSinceFlush++;

            if (processedSinceFlush >= CheckpointInterval)
            {
                // Canonical write MUST precede the checkpoint write: if a crash happens between the
                // two, the worst case is an id missing from the checkpoint (safe — it is simply
                // re-graded next run), never a checkpoint claiming "done" for a mutation that was
                // never persisted.
                await writer.WriteAsync(path, file with { Entities = entities }, ct);
                await WriteCheckpointAsync(checkpointPath, doneIds, ct);
                processedSinceFlush = 0;
            }
        }

        // Final flush: guarantees the canonical file reflects every mutation made in this run, even
        // when the last (partial) batch never hit the interval above.
        await writer.WriteAsync(path, file with { Entities = entities }, ct);

        foreach (var id in changedIds)
            await orchestrator.ReindexEntityAsync(bookId, id, ct);

        DeleteCheckpointIfExists(checkpointPath);

        var promoted = 0;
        var markedUngrounded = 0;
        var stillFlagged = 0;
        foreach (var id in flaggedIds)
        {
            var final = entities.First(e => string.Equals(e.Id, id, StringComparison.Ordinal));
            if (final.Disposition == EntityDisposition.Accepted) promoted++;
            else if (final.Disposition == EntityDisposition.Ungrounded) markedUngrounded++;
            else stillFlagged++;
        }

        return new RegroundResult(
            Scanned: flaggedIds.Count,
            Promoted: promoted,
            MarkedUngrounded: markedUngrounded,
            StillFlagged: stillFlagged,
            Tier2Invoked: tier2Invoked);
    }

    private static bool IsFlagged(EntityEnvelope e) =>
        e.Disposition == EntityDisposition.NeedsReview || e.NeedsReview;

    private async Task<string> AssembleSourceProseAsync(EntityEnvelope entity, CancellationToken ct)
    {
        var entityText = string.IsNullOrEmpty(entity.CanonicalText)
            ? entity.Fields.GetRawText()
            : entity.CanonicalText;

        var embedded = await embeddings.EmbedAsync([entityText], ct);
        var vector = embedded[0];

        var filter = new Filter();
        filter.Must.Add(KeywordCondition(QdrantPayloadFields.SourceBook, entity.SourceBook));
        if (entity.Page is { } p)
        {
            var window = groundingOptions.Value.PageWindow;
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = QdrantPayloadFields.PageNumber,
                    Range = new Qdrant.Client.Grpc.Range { Gte = p - window, Lte = p + window },
                },
            });
        }

        var hits = await qdrant.SearchAsync(
            qdrantOptions.Value.BlocksCollectionName,
            vector,
            filter: filter,
            limit: SourceProseHitLimit,
            cancellationToken: ct);

        return hits.Count == 0
            ? string.Empty
            : string.Join("\n\n", hits.Select(h => QdrantPayloadMapper.GetText(h.Payload)));
    }

    private static Condition KeywordCondition(string key, string value) => new()
    {
        Field = new FieldCondition { Key = key, Match = new Match { Keyword = value } },
    };

    private static async Task<HashSet<string>> LoadCheckpointAsync(string checkpointPath, CancellationToken ct)
    {
        try
        {
            await using var s = File.OpenRead(checkpointPath);
            var ids = await JsonSerializer.DeserializeAsync<List<string>>(s, CheckpointJsonOptions, ct) ?? [];
            return new HashSet<string>(ids, StringComparer.Ordinal);
        }
        catch (FileNotFoundException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static async Task WriteCheckpointAsync(string checkpointPath, HashSet<string> doneIds, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(checkpointPath) ?? ".";
        Directory.CreateDirectory(dir);
        var tmp = checkpointPath + ".tmp";
        await using (var s = File.Create(tmp))
            await JsonSerializer.SerializeAsync(s, doneIds.ToList(), CheckpointJsonOptions, ct);
        try
        {
            File.Move(tmp, checkpointPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }

    private static void DeleteCheckpointIfExists(string checkpointPath)
    {
        try { File.Delete(checkpointPath); } catch { /* best effort */ }
    }
}
