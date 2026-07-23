using System.Collections.Concurrent;
using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Entities;

public sealed class CanonicalJsonSchemaException(string message) : Exception(message);

public sealed class CanonicalJsonLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    // Canonical JSON files run 2-3.5 MB and are re-parsed on every admin-path call; writers use an
    // atomic tmp+rename so the file's mtime changes on every real write, making it a safe cache-validity
    // key (audit P3). Keyed by Path.GetFullPath(path) (see NormalizePathKey) — NOT the raw path
    // string — so a LoadAsync and a later WriteAsync/Invalidate for the same file always agree on
    // the cache key even if the two callers spelled the path differently (e.g. one with a "./"
    // relative segment). A raw-string key previously let such spelling differences make
    // Invalidate silently miss the entry it needed to evict.
    //
    // On a WSL2 bind mount, File.GetLastWriteTimeUtc can return a STALE mtime immediately after a
    // real write, so a rapid load-modify-write sequence on the same path could serve a stale cached
    // instance on the next load and silently clobber the previous write's changes. Two independent
    // defenses close this: (1) the cache key also requires the file LENGTH to match — an append/edit
    // almost always changes length even when the reported mtime doesn't; (2) CanonicalJsonWriter.
    // WriteAsync calls Invalidate(path) after every successful write, unconditionally evicting the
    // entry regardless of what the FS reports for mtime/length. The cache is `static` (shared across
    // all CanonicalJsonLoader instances in the process), so Invalidate is a static method usable by
    // any caller without needing the same loader instance injected.
    private static readonly ConcurrentDictionary<string, (DateTime MtimeUtc, long Length, CanonicalJsonFile File)> Cache = new();

    /// <summary>
    /// Evicts the cache entry for <paramref name="path"/>, if any, forcing the next
    /// <see cref="LoadAsync"/> call for that path to re-read from disk. Call this after writing to
    /// <paramref name="path"/> — read-after-write consistency must not depend on the filesystem's
    /// mtime granularity (see class remarks).
    /// </summary>
    public static void Invalidate(string path) => Cache.TryRemove(NormalizePathKey(path), out _);

    // Cache key normalization (COR-cache-key-gap): callers are NOT guaranteed to pass byte-identical
    // path strings between a LoadAsync and a later WriteAsync/Invalidate for the SAME file (e.g. one
    // caller's path carries a "./" or other relative segment). Path.GetFullPath resolves such
    // differences to the same canonical absolute string, so the cache key and the eviction key
    // always match for the same underlying file regardless of how the path was spelled.
    private static string NormalizePathKey(string path) => Path.GetFullPath(path);

    public async Task<CanonicalJsonFile> LoadAsync(string path, CancellationToken ct)
    {
        var cacheKey = NormalizePathKey(path);
        var mtimeUtc = File.GetLastWriteTimeUtc(path);
        var length = new FileInfo(path).Length;
        if (Cache.TryGetValue(cacheKey, out var cached) && cached.MtimeUtc == mtimeUtc && cached.Length == length)
            return cached.File;

        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<CanonicalJsonFile>(stream, JsonOptions, ct)
                   ?? throw new CanonicalJsonSchemaException($"Failed to deserialise canonical JSON at {path}");

        if (file.SchemaVersion != CanonicalJsonSchema.CurrentVersion)
            throw new CanonicalJsonSchemaException(
                $"Unsupported schemaVersion '{file.SchemaVersion}' (expected '{CanonicalJsonSchema.CurrentVersion}') in {path}");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in file.Entities)
        {
            if (string.IsNullOrEmpty(e.Id))
                throw new CanonicalJsonSchemaException($"Entity with empty id in {path}");
            if (!seen.Add(e.Id))
                throw new CanonicalJsonSchemaException($"duplicate id '{e.Id}' in {path}");
        }

        var seenTables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in file.Tables)
        {
            if (string.IsNullOrEmpty(t.Id))
                throw new CanonicalJsonSchemaException($"Table with empty id in {path}");
            if (!seenTables.Add(t.Id))
                throw new CanonicalJsonSchemaException($"duplicate table id '{t.Id}' in {path}");
        }

        var seenChoiceSets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cs in file.ChoiceSets)
        {
            if (string.IsNullOrEmpty(cs.Id))
                throw new CanonicalJsonSchemaException($"ChoiceSet with empty id in {path}");
            if (!seenChoiceSets.Add(cs.Id))
                throw new CanonicalJsonSchemaException($"duplicate choiceSet id '{cs.Id}' in {path}");
            foreach (var opt in cs.Options)
            {
                if (!seenTables.Contains(opt.TableId))
                    throw new CanonicalJsonSchemaException(
                        $"ChoiceOption key '{opt.Key}' in choiceSet '{cs.Id}' references unknown tableId '{opt.TableId}' in {path}");
            }
        }

        var promoted = file.Entities.Select(PromoteKeywords).ToList();
        var result = file with { Entities = promoted };
        Cache[cacheKey] = (mtimeUtc, length, result);
        return result;
    }

    private static EntityEnvelope PromoteKeywords(EntityEnvelope entity)
    {
        if (entity.Keywords.Count > 0) return entity;
        if (entity.Fields.ValueKind != JsonValueKind.Object) return entity;
        if (!entity.Fields.TryGetProperty("keywords", out var kws) || kws.ValueKind != JsonValueKind.Array) return entity;
        var keywords = kws.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
        return keywords.Count > 0 ? entity with { Keywords = keywords } : entity;
    }

    public TFields DeserialiseFields<TFields>(EntityEnvelope envelope)
    {
        return envelope.Fields.Deserialize<TFields>(JsonOptions)
               ?? throw new CanonicalJsonSchemaException(
                   $"Failed to deserialise fields for entity {envelope.Id} as {typeof(TFields).Name}");
    }
}