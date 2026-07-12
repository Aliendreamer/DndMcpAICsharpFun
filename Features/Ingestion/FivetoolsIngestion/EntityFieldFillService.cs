using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>
/// Result of a per-book 5etools field-fill pass. <see cref="FilledByType"/> counts entities whose
/// <c>Fields</c> actually changed (fill-missing-only merge), keyed by <see cref="EntityType"/>;
/// <see cref="EntitiesTouched"/> is the total across all types. A book with no
/// <see cref="IngestionRecord.FivetoolsSourceKey"/> yields <see cref="NoSourceKey"/> (no write); a
/// source key whose canonical file doesn't exist yet yields <see cref="NoCanonical"/> (no write).
/// </summary>
public sealed record FieldFillResult(
    bool HasSourceKey,
    string? CanonicalPath,
    IReadOnlyDictionary<EntityType, int> FilledByType,
    int EntitiesTouched)
{
    public static FieldFillResult NoSourceKey { get; } =
        new(false, null, new Dictionary<EntityType, int>(), 0);

    public static FieldFillResult NoCanonical(string canonicalPath) =>
        new(true, canonicalPath, new Dictionary<EntityType, int>(), 0);
}

/// <summary>
/// Per-book 5etools field-fill: for a book with a <see cref="IngestionRecord.FivetoolsSourceKey"/>, merges
/// allowlisted (<see cref="FieldFillAllowlist"/>) structured 5etools fields onto each canonical entity via
/// <see cref="FivetoolsFieldMerger.Merge"/> — fill-missing-only, never overwriting an extraction/human field
/// and never touching a <c>DataSource == "manual"</c> entity. The 5etools roster is indexed by
/// <see cref="EntityNameIndex.Normalize"/>d name, scoped to elements whose <c>source</c> matches the book's
/// key, using <see cref="FivetoolsSourceRegistry"/> (file + array key per type) and
/// <see cref="FivetoolsMapperRegistry"/> (raw 5etools record as <c>Fields</c>). Writes the canonical only if
/// something actually changed.
/// </summary>
public sealed class EntityFieldFillService
{
    private const string StandardFivetoolsBase = "5etools";

    private readonly CanonicalJsonLoader _loader;
    private readonly CanonicalJsonWriter _writer;
    private readonly string _canonicalDirectory;
    private readonly string _fivetoolsDirectory;

    public EntityFieldFillService(
        CanonicalJsonLoader loader,
        CanonicalJsonWriter writer,
        string canonicalDirectory,
        string fivetoolsDirectory)
    {
        _loader = loader;
        _writer = writer;
        _canonicalDirectory = canonicalDirectory;
        _fivetoolsDirectory = fivetoolsDirectory;
    }

    /// <summary>
    /// Fills allowlisted structured fields onto the book's canonical entities from the 5etools roster
    /// matching <see cref="IngestionRecord.FivetoolsSourceKey"/>. No source key or no canonical file yet
    /// yields a no-op result (no write). Idempotent: a second run with the same inputs changes nothing.
    /// </summary>
    public async Task<FieldFillResult> FillAsync(IngestionRecord record, CancellationToken ct)
    {
        var key = record.FivetoolsSourceKey;
        if (string.IsNullOrWhiteSpace(key))
            return FieldFillResult.NoSourceKey;

        var slug = EntityIdSlug.BookSlug(key);
        var path = Path.Combine(_canonicalDirectory, slug + ".json");
        if (!File.Exists(path))
            return FieldFillResult.NoCanonical(path);

        var file = await _loader.LoadAsync(path, ct);

        // Only index types that both have an allowlist and actually appear in this book's canonical
        // entities — avoids opening 5etools files for types the book has none of.
        var typesNeeded = new HashSet<EntityType>(
            file.Entities.Select(e => e.Type).Where(t => FieldFillAllowlist.For(t) is not null));

        var fivetoolsIndex = BuildFivetoolsIndex(typesNeeded, key, ct);

        var entities = new List<EntityEnvelope>(file.Entities.Count);
        var filledByType = new Dictionary<EntityType, int>();
        var anyChanged = false;

        foreach (var e in file.Entities)
        {
            var allow = FieldFillAllowlist.For(e.Type);
            if (allow is null
                || string.Equals(e.DataSource, "manual", StringComparison.Ordinal)
                || !fivetoolsIndex.TryGetValue(e.Type, out var byName)
                || !byName.TryGetValue(EntityNameIndex.Normalize(e.Name), out var fiveFields))
            {
                entities.Add(e);
                continue;
            }

            var (merged, changed) = FivetoolsFieldMerger.Merge(e.Fields, allow, fiveFields);
            entities.Add(changed ? e with { Fields = merged } : e);
            if (changed)
            {
                anyChanged = true;
                filledByType[e.Type] = filledByType.GetValueOrDefault(e.Type) + 1;
            }
        }

        if (anyChanged)
            await _writer.WriteAsync(path, file with { Entities = entities }, ct);

        return new FieldFillResult(true, path, filledByType, filledByType.Values.Sum());
    }

    /// <summary>
    /// Builds a per-type <c>Normalize(name) → 5etools Fields</c> index scoped to <paramref name="sourceKey"/>,
    /// walking only the <see cref="FivetoolsSourceRegistry"/> entries whose type is in
    /// <paramref name="typesNeeded"/>. Each entry's registry-relative path is remapped onto the configured
    /// 5etools directory exactly like <see cref="FivetoolsRecordIndex.BuildAsync"/> does, so a custom data
    /// root (e.g. a test fixture) is honoured instead of the compile-time "5etools" default.
    /// </summary>
    private Dictionary<EntityType, Dictionary<string, JsonElement>> BuildFivetoolsIndex(
        HashSet<EntityType> typesNeeded, string sourceKey, CancellationToken ct)
    {
        var result = new Dictionary<EntityType, Dictionary<string, JsonElement>>();
        if (typesNeeded.Count == 0) return result;

        foreach (var entry in FivetoolsSourceRegistry.AllEntries)
        {
            ct.ThrowIfCancellationRequested();
            if (!typesNeeded.Contains(entry.EntityType)) continue;
            if (!FivetoolsMapperRegistry.Mappers.TryGetValue(entry.EntityType, out var mapper)) continue;

            var filePath = ResolvePath(entry.RelativePath);
            if (!File.Exists(filePath)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllBytes(filePath)); }
            catch (JsonException) { continue; }
            using (doc)
            {
                if (!doc.RootElement.TryGetProperty(entry.JsonArrayKey, out var arr)
                    || arr.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in arr.EnumerateArray())
                {
                    if (!item.TryGetProperty("source", out var srcProp)
                        || srcProp.ValueKind != JsonValueKind.String
                        || !string.Equals(srcProp.GetString(), sourceKey, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var envelope = mapper.Map(item);
                    if (envelope is null) continue;

                    if (!result.TryGetValue(entry.EntityType, out var byName))
                        result[entry.EntityType] = byName = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

                    byName[EntityNameIndex.Normalize(envelope.Name)] = envelope.Fields;
                }
            }
        }

        return result;
    }

    /// <summary>Remaps a registry <c>"5etools/..."</c>-relative path onto the configured 5etools directory.</summary>
    private string ResolvePath(string relativePath)
    {
        if (relativePath.StartsWith(StandardFivetoolsBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(StandardFivetoolsBase + "/", StringComparison.OrdinalIgnoreCase))
        {
            var tail = relativePath[(StandardFivetoolsBase.Length + 1)..];
            return Path.Combine(_fivetoolsDirectory, tail);
        }

        return relativePath;
    }
}
