using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Outcome of a 5etools import: what was written and what was deliberately skipped.</summary>
public sealed record FivetoolsImportResult(
    int Mapped,
    int Upserted,
    int SkippedProgrammaticBook,
    int SkippedExisting,
    IReadOnlyList<string> ProgrammaticBooks);

public sealed class FivetoolsIngestionService(
    IEntityVectorStore store,
    IEmbeddingService embeddings,
    EntityCanonicalTextDispatcher dispatcher,
    IOptions<EntityIngestionOptions> entityOptions,
    ILogger<FivetoolsIngestionService> logger)
{
    public async Task<FivetoolsImportResult> ImportAllAsync(CancellationToken ct = default)
    {
        var allEnvelopes = new List<EntityEnvelope>();

        foreach (var entry in FivetoolsSourceRegistry.AllEntries)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(entry.RelativePath))
            {
                logger.LogWarning("5etools file not found, skipping: {Path}", entry.RelativePath);
                continue;
            }
            if (!FivetoolsMapperRegistry.Mappers.TryGetValue(entry.EntityType, out var mapper))
            {
                logger.LogWarning("No mapper for entity type {Type}, skipping {Path}", entry.EntityType, entry.RelativePath);
                continue;
            }

            await using var stream = File.OpenRead(entry.RelativePath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty(entry.JsonArrayKey, out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in arr.EnumerateArray())
            {
                var envelope = mapper.Map(item);
                if (envelope is not null)
                    allEnvelopes.Add(envelope);
            }
        }

        logger.LogInformation("5etools import: {Count} entities mapped", allEnvelopes.Count);
        return await IngestEnvelopesAsync(allEnvelopes, ct);
    }

    public async Task<FivetoolsImportResult> IngestEnvelopesAsync(
        IReadOnlyList<EntityEnvelope> envelopes, CancellationToken ct = default)
    {
        if (envelopes.Count == 0) return new FivetoolsImportResult(0, 0, 0, 0, []);

        // A book with a programmatic canonical file is owned by the PDF extractor: its flagged entities are
        // deliberately absent from Qdrant, so an id-based guard alone would let unflagged 5etools points in
        // under those ids. Drop every envelope of such a book.
        var programmaticBooks = envelopes.Select(e => e.SourceBook).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(IsProgrammaticBook).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = envelopes.Where(e => !programmaticBooks.Contains(e.SourceBook)).ToList();
        var skippedProgrammatic = envelopes.Count - candidates.Count;
        if (skippedProgrammatic > 0)
            logger.LogInformation("5etools import: skipped {Count} entities of programmatic books ({Books})",
                skippedProgrammatic, string.Join(", ", programmaticBooks));

        var ids = candidates.Select(e => e.Id).ToList();
        var existingSources = ids.Count == 0
            ? new Dictionary<string, string>()
            : await store.GetDataSourcesAsync(ids, ct);
        var toIngest = candidates
            .Where(e => !existingSources.TryGetValue(e.Id, out var src) || src != "manual" && src != DataSources.Programmatic)
            .ToList();

        var skippedExisting = candidates.Count - toIngest.Count;
        if (skippedExisting > 0)
            logger.LogInformation("Skipped {Count} manually-corrected or programmatic entities", skippedExisting);

        var bookList = programmaticBooks.Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (toIngest.Count == 0)
            return new FivetoolsImportResult(envelopes.Count, 0, skippedProgrammatic, skippedExisting, bookList);

        var renderedEnvelopes = toIngest.Select(e =>
        {
            try { return e with { CanonicalText = dispatcher.Render(e) }; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to render canonical text for {Id}", e.Id);
                return e;
            }
        }).ToList();

        var texts = renderedEnvelopes.Select(e => e.CanonicalText).ToList();
        IList<float[]> vectors = texts.Count == 0
            ? Array.Empty<float[]>()
            : await embeddings.EmbedAsync(texts, ct);

        if (vectors.Count != renderedEnvelopes.Count)
            throw new InvalidOperationException(
                $"EmbedAsync returned {vectors.Count} vectors for {renderedEnvelopes.Count} texts.");

        var points = renderedEnvelopes
            .Select((e, i) => new EntityPoint(e, vectors[i], FivetoolsImportedPoints.FileHash(e.SourceBook)))
            .ToList();

        await store.UpsertAsync(points, ct);
        logger.LogInformation("5etools import: upserted {Count} entities", points.Count);
        return new FivetoolsImportResult(envelopes.Count, points.Count, skippedProgrammatic, skippedExisting, bookList);
    }

    private bool IsProgrammaticBook(string sourceBook) =>
        !string.IsNullOrWhiteSpace(sourceBook)
        && ProgrammaticCanonicalGuard.FileHasProgrammatic(
            Path.Combine(entityOptions.Value.CanonicalDirectory, EntityIdSlug.BookSlug(sourceBook) + ".json"));


    /// <summary>
    /// Builds a lookup of (name, sourceBook) → EntityType from all available 5etools source files.
    /// Used by the canonical type-fixer to correct LLM-assigned entity types.
    /// </summary>
    public async Task<IReadOnlyDictionary<(string name, string source), EntityType>> BuildTypeLookupAsync(
        CancellationToken ct = default)
    {
        // Use last-writer-wins: if the same (name, source) appears in multiple entries
        // (e.g. a subrace entry overrides a race entry for the same name), we keep the
        // more-specific type that was registered last in the registry.
        var lookup = new Dictionary<(string, string), EntityType>();

        foreach (var entry in FivetoolsSourceRegistry.AllEntries)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(entry.RelativePath)) continue;
            if (!FivetoolsMapperRegistry.Mappers.TryGetValue(entry.EntityType, out var mapper)) continue;

            await using var stream = File.OpenRead(entry.RelativePath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty(entry.JsonArrayKey, out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in arr.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out var nameProp)
                    || nameProp.ValueKind != JsonValueKind.String)
                    continue;
                var name = nameProp.GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var source = item.TryGetProperty("source", out var src)
                    && src.ValueKind == JsonValueKind.String
                    ? src.GetString()! : "Unknown";

                lookup[(name.ToLowerInvariant(), source.ToUpperInvariant())] = entry.EntityType;
            }
        }

        return lookup;
    }
}