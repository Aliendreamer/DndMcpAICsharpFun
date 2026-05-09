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

    public async Task<CanonicalJsonFile> LoadAsync(string path, CancellationToken ct)
    {
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

        var promoted = file.Entities.Select(PromoteKeywords).ToList();
        return file with { Entities = promoted };
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
