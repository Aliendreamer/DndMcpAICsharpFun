using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

namespace DndMcpAICsharpFun.Tests.TestDoubles;

/// <summary>
/// Shared in-memory <see cref="IEntityVectorStore"/> test double that combines:
/// <list type="bullet">
///   <item>Real upsert / delete-by-filehash semantics (mirrors Qdrant behaviour)</item>
///   <item>Call-count recording for upsert and delete operations</item>
/// </list>
/// Used by EntityIngestionSelfDeleteTests (semantic correctness) and
/// ReindexEntityTests (call-count assertions).
/// </summary>
public sealed class RecordingEntityVectorStore : IEntityVectorStore
{
    private readonly Dictionary<string, (EntityEnvelope Env, string FileHash)> _store =
        new(StringComparer.Ordinal);

    // ── Recorded calls ────────────────────────────────────────────────────────

    /// <summary>Each element is the list of points passed in one UpsertAsync call.</summary>
    public List<IList<EntityPoint>> UpsertCalls { get; } = [];

    /// <summary>Total number of DeleteByFileHashExceptAsync calls received.</summary>
    public int DeleteByFileHashExceptCallCount { get; private set; }

    /// <summary>Total number of DeleteByFileHashAsync calls received.</summary>
    public int DeleteByFileHashCallCount { get; private set; }

    /// <summary>Each element is the id collection passed in one DeleteByIdsAsync call.</summary>
    public List<IReadOnlyCollection<string>> DeleteByIdsCalls { get; } = [];

    // ── Convenience view ──────────────────────────────────────────────────────

    /// <summary>IDs of all currently stored points (live view).</summary>
    public IReadOnlyCollection<string> Ids => _store.Keys;

    // ── IEntityVectorStore ────────────────────────────────────────────────────

    public Task UpsertAsync(IList<EntityPoint> points, CancellationToken ct = default)
    {
        UpsertCalls.Add(points);
        foreach (var p in points)
            _store[p.Envelope.Id] = (p.Envelope, p.FileHash);
        return Task.CompletedTask;
    }

    public Task DeleteByFileHashAsync(string fileHash, CancellationToken ct = default)
    {
        DeleteByFileHashCallCount++;
        foreach (var id in _store
                     .Where(kv => kv.Value.FileHash == fileHash)
                     .Select(kv => kv.Key).ToList())
            _store.Remove(id);
        return Task.CompletedTask;
    }

    public Task DeleteByFileHashExceptAsync(
        string fileHash, IReadOnlyCollection<string> keepIds, CancellationToken ct = default)
    {
        DeleteByFileHashExceptCallCount++;
        var keep = keepIds.ToHashSet(StringComparer.Ordinal);
        foreach (var id in _store
                     .Where(kv => kv.Value.FileHash == fileHash && !keep.Contains(kv.Key))
                     .Select(kv => kv.Key).ToList())
            _store.Remove(id);
        return Task.CompletedTask;
    }

    public Task<EntityEnvelope?> GetByIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue(id, out var v) ? v.Env : (EntityEnvelope?)null);

    public Task<IReadOnlyDictionary<string, EntityEnvelope>> GetByIdsAsync(
        IReadOnlyList<string> entityIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, EntityEnvelope>>(
            _store.Where(kv => entityIds.Contains(kv.Key))
                  .ToDictionary(kv => kv.Key, kv => kv.Value.Env, StringComparer.Ordinal));

    public Task<IList<EntitySearchHit>> SearchAsync(
        float[] queryVector, EntityFilters filters, int topK, CancellationToken ct = default)
        => Task.FromResult<IList<EntitySearchHit>>(Array.Empty<EntitySearchHit>());

    public Task<IReadOnlyDictionary<string, string>> GetDataSourcesAsync(
        IReadOnlyList<string> entityIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(StringComparer.Ordinal));


    public Task<IReadOnlyList<EntitySearchHit>> ScrollAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EntitySearchHit>>(
            _store.Select(kv => new EntitySearchHit(kv.Value.Env, 0f, "pt-" + kv.Key)).ToList());

    public Task DeleteByIdsAsync(IReadOnlyCollection<string> entityIds, CancellationToken ct = default)
    {
        DeleteByIdsCalls.Add(entityIds);
        foreach (var id in entityIds) _store.Remove(id);
        return Task.CompletedTask;
    }
}
