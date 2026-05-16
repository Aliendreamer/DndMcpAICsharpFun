using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin;

public sealed class CanonicalTypeFixerService(IOptions<EntityIngestionOptions> options)
{
    private readonly string _canonicalDirectory = options.Value.CanonicalDirectory;

    public record FixResult(int Fixed, int Unmatched, int CrossRefsUpdated);

    public async Task<FixResult> FixTypesAsync(
        string bookSlug,
        IReadOnlyDictionary<(string name, string source), EntityType> lookup,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bookSlug) ||
            System.IO.Path.GetFileName(bookSlug) != bookSlug)
            throw new ArgumentException("Invalid bookSlug.", nameof(bookSlug));

        var path = Path.Combine(_canonicalDirectory, bookSlug + ".json");
        if (!File.Exists(path)) throw new FileNotFoundException(path);

        var json = await File.ReadAllTextAsync(path, ct);
        var doc = JsonNode.Parse(json)
            ?? throw new InvalidOperationException($"Canonical file at {path} parsed as JSON null.");
        var entitiesNode = doc["entities"]
            ?? throw new InvalidOperationException($"Canonical file at {path} has no 'entities' array.");
        var entities = entitiesNode.AsArray();

        int fixed_ = 0, unmatched = 0, xrefUpdated = 0;
        var renames = new Dictionary<string, string>();

        foreach (var entity in entities)
        {
            if (entity is null) continue;
            var name   = entity["name"]?.GetValue<string>();
            var source = entity["sourceBook"]?.GetValue<string>();
            if (name is null || source is null) continue;

            var key = (name.ToLowerInvariant(), source.ToUpperInvariant());

            if (!lookup.TryGetValue(key, out var correctType))
            {
                unmatched++;
                continue;
            }

            var currentType = entity["type"]?.GetValue<string>();
            var oldId       = entity["id"]?.GetValue<string>();
            if (currentType is null || oldId is null) continue;

            if (string.Equals(currentType, correctType.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            var newId = ReplaceTypeSlug(oldId, correctType);
            if (oldId == newId) continue;

            renames[oldId] = newId;
            entity["type"] = correctType.ToString();
            entity["id"] = newId;
            fixed_++;
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        if (renames.Count == 0)
        {
            await File.WriteAllTextAsync(path, doc.ToJsonString(jsonOptions), ct);
            return new FixResult(fixed_, unmatched, 0);
        }

        // Rewrite cross-references: string-replace all old IDs in the serialized JSON
        var updated = doc.ToJsonString(jsonOptions);
        foreach (var (oldId, newId) in renames)
        {
            var before = updated.Length;
            updated = updated.Replace($"\"{oldId}\"", $"\"{newId}\"");
            if (updated.Length != before) xrefUpdated++;
        }

        await File.WriteAllTextAsync(path, updated, ct);
        return new FixResult(fixed_, unmatched, xrefUpdated);
    }

    private static string ReplaceTypeSlug(string id, EntityType newType)
    {
        var parts = id.Split('.');
        if (parts.Length < 3) return id;
        parts[1] = newType.ToString().ToLowerInvariant();
        return string.Join('.', parts);
    }
}
