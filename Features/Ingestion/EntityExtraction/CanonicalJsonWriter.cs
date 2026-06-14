using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class CanonicalJsonWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    // Per-book-path write lock — serialises concurrent resolves on the same file.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>
        _fileLocks = new(StringComparer.OrdinalIgnoreCase);

    private SemaphoreSlim LockFor(string path) =>
        _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

    public async ValueTask WriteAsync(string path, CanonicalJsonFile file, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, file, WriteOptions, ct);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* swallow cleanup */ }
            throw;
        }
    }

    /// <summary>
    /// Loads the canonical file at <paramref name="path"/>, mutates the entity with
    /// <paramref name="entityId"/> (clear <c>NeedsReview</c>; optionally set
    /// <paramref name="name"/> and shallow-merge <paramref name="fieldsToMerge"/> keys),
    /// then writes the file back atomically.  Uses a per-path lock to serialise concurrent
    /// writes on the same book.
    /// </summary>
    /// <param name="path">Absolute or relative path to the canonical JSON file.</param>
    /// <param name="entityId">The entity <c>id</c> to mutate.</param>
    /// <param name="name">When non-null, replaces the entity name (id is kept stable — v1 limitation).</param>
    /// <param name="fieldsToMerge">
    ///   When non-null, the top-level keys of this <see cref="System.Text.Json.JsonElement"/> are
    ///   shallow-merged into the entity's existing <c>fields</c> object (provided keys win).
    /// </param>
    /// <param name="loader">
    ///   The <see cref="CanonicalJsonLoader"/> used to load (and validate) the file before mutation.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///   <c>true</c> when the entity was found and written; <c>false</c> when the id is absent
    ///   from the file (caller should treat this as not-found).
    /// </returns>
    public async ValueTask<bool> PatchEntityAsync(
        string path,
        string entityId,
        string? name,
        System.Text.Json.JsonElement? fieldsToMerge,
        Features.Entities.CanonicalJsonLoader loader,
        CancellationToken ct)
    {
        var sem = LockFor(path);
        await sem.WaitAsync(ct);
        try
        {
            var file = await loader.LoadAsync(path, ct);
            var idx = -1;
            for (int i = 0; i < file.Entities.Count; i++)
            {
                if (string.Equals(file.Entities[i].Id, entityId, StringComparison.Ordinal))
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0) return false;

            var original = file.Entities[idx];
            var patched  = original with { NeedsReview = false };

            // v1: name edit keeps the existing id stable even if the slug would change.
            if (name is not null)
                patched = patched with { Name = name };

            if (fieldsToMerge.HasValue &&
                fieldsToMerge.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                patched = patched with { Fields = ShallowMergeFields(original.Fields, fieldsToMerge.Value) };
            }

            var updatedEntities = file.Entities.ToList();
            updatedEntities[idx] = patched;
            var updatedFile = file with { Entities = updatedEntities };

            await WriteAsync(path, updatedFile, ct);
            return true;
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Shallow-merge: all top-level keys from <paramref name="incoming"/> override
    /// the same key in <paramref name="existing"/>; all other keys from existing are kept.
    /// Returns a fresh <see cref="System.Text.Json.JsonElement"/>.
    /// </summary>
    private static System.Text.Json.JsonElement ShallowMergeFields(
        System.Text.Json.JsonElement existing,
        System.Text.Json.JsonElement incoming)
    {
        var result = new System.Text.Json.Nodes.JsonObject();

        // Copy existing keys first.
        if (existing.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var prop in existing.EnumerateObject())
                result[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
        }

        // Overwrite / add keys from incoming.
        foreach (var prop in incoming.EnumerateObject())
            result[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());

        var json = result.ToJsonString();
        return System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
    }
}
