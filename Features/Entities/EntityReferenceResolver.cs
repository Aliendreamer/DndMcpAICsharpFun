using System.Collections.Frozen;
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Entities;

public sealed record EntityReferenceWarning(string SourceEntityId, string FieldPath, string MissingTargetId);

public sealed class EntityReferenceResolver
{
    private static readonly FrozenSet<string> KnownTypeSlugs =
        new HashSet<string>(Enum.GetNames<EntityType>().Select(n => n.ToLowerInvariant()), StringComparer.Ordinal).ToFrozenSet(StringComparer.Ordinal);

    public IEnumerable<EntityReferenceWarning> Resolve(IReadOnlyList<EntityEnvelope> entities)
    {
        var ids = entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            foreach (var (path, value) in WalkStringValues(entity.Fields, prefix: "fields"))
            {
                if (LooksLikeEntityReference(value) && !ids.Contains(value))
                    yield return new EntityReferenceWarning(entity.Id, path, value);
            }
        }
    }

    private static bool LooksLikeEntityReference(string s)
    {
        if (s.Count(c => c == '.') != 2) return false;
        if (!s.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-')) return false;
        var parts = s.Split('.');
        return parts.Length == 3 && KnownTypeSlugs.Contains(parts[1]);
    }

    private static IEnumerable<(string Path, string Value)> WalkStringValues(JsonElement element, string prefix)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                    foreach (var pair in WalkStringValues(prop.Value, $"{prefix}.{prop.Name}"))
                        yield return pair;
                break;
            case JsonValueKind.Array:
                int idx = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var pair in WalkStringValues(item, $"{prefix}[{idx}]"))
                        yield return pair;
                    idx++;
                }
                break;
            case JsonValueKind.String:
                yield return (prefix, element.GetString() ?? "");
                break;
        }
    }
}
