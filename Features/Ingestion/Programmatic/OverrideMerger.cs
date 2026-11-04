using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

public sealed record MergeResult(IReadOnlyList<EntityEnvelope> Entities, IReadOnlyList<string> OrphanedEntityIds);

/// <summary>Re-applies admin overrides over freshly extracted entities and derives each entity's disposition.</summary>
public static class OverrideMerger
{
    public const string DriftCheck = "override-drift";
    public const string UnappliedCheck = "override-unapplied";
    private const string FieldsPrefix = "fields.";

    public static MergeResult Apply(IReadOnlyList<EntityEnvelope> extracted, EntityOverrides o)
    {
        var ids = extracted.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var orphans = o.Overrides.Select(x => x.EntityId)
            .Concat(o.Approvals.Select(a => a.EntityId))
            .Where(id => !ids.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var result = new List<EntityEnvelope>(extracted.Count);
        foreach (var original in extracted)
        {
            var mine = o.Overrides.Where(x => x.EntityId == original.Id).ToList();
            var approved = o.Approvals.Any(a => a.EntityId == original.Id);
            var e = original;
            // In file order: for the same field the last override wins.
            foreach (var ov in mine)
                e = ApplyOne(original, e, ov);

            if (mine.Count > 0 || approved || e.Checks.Count > 0)
                e = SetDisposition(e, approved);
            result.Add(e);
        }
        return new MergeResult(result, orphans);
    }

    public static EntityEnvelope SetDisposition(EntityEnvelope e, bool approved)
    {
        // An approval waives ordinary failed checks but never an unresolved override drift
        // or an override that could not be applied.
        var blocked = e.Checks.Any(c => !c.Passed
            && (!approved || c.Name is DriftCheck or UnappliedCheck));
        return blocked
            ? e with { Disposition = EntityDisposition.NeedsReview, NeedsReview = true }
            : e with { Disposition = EntityDisposition.Accepted, NeedsReview = false };
    }

    /// <summary>
    /// True for <c>name</c>, <c>canonicalText</c>, and <c>fields.…</c> paths that resolve to an existing value
    /// (including JSON null), or to a new leaf key directly under an existing object. A missing parent is invalid.
    /// </summary>
    public static bool ValidatePath(EntityEnvelope e, string field)
    {
        if (field is "name" or "canonicalText") return true;
        if (!field.StartsWith(FieldsPrefix, StringComparison.Ordinal)) return false;
        var segments = field[FieldsPrefix.Length..].Split('.');
        if (segments.Any(s => s.Length == 0)) return false;
        var root = FieldsNode(e);
        if (TryWalk(root, segments, out _)) return true;
        return segments.Length >= 1
            && TryWalk(root, segments[..^1], out var parent)
            && parent is JsonObject;
    }

    /// <summary>
    /// Drift compares with <see cref="JsonNode.DeepEquals"/>: object key order is ignored, array order matters,
    /// and numbers compare by value, so 2 and 2.0 are equal; genuinely different values drift.
    /// </summary>
    private static EntityEnvelope ApplyOne(EntityEnvelope original, EntityEnvelope e, FieldOverride ov)
    {
        // Drift is judged against the original extraction, not an earlier override on the same field.
        var current = GetCurrent(original, ov.Field);
        var updated = Set(e, ov.Field, ov.Value, out var why);
        if (updated is null)
        {
            return e with
            {
                Checks = [.. e.Checks, new EntityCheck(UnappliedCheck, false,
                    $"override at {ov.Field} could not be applied: {why}")],
            };
        }

        if (ov.Extracted is { } baseline
            && !JsonNode.DeepEquals(JsonNode.Parse(baseline.GetRawText()), current))
        {
            var marker = $" at {ov.Field}:";
            if (!updated.Checks.Any(c => c.Name == DriftCheck && c.Reason.Contains(marker)))
            {
                updated = updated with
                {
                    Checks = [.. updated.Checks, new EntityCheck(DriftCheck, false,
                        DriftPrefix(ov.Field, baseline) + (current?.ToJsonString() ?? "null"))],
                };
            }
        }
        return updated;
    }

    private static JsonNode? GetCurrent(EntityEnvelope e, string field)
    {
        switch (field)
        {
            case "name": return JsonValue.Create(e.Name);
            case "canonicalText": return JsonValue.Create(e.CanonicalText);
        }
        if (!field.StartsWith(FieldsPrefix, StringComparison.Ordinal)) return null;
        return TryWalk(FieldsNode(e), field[FieldsPrefix.Length..].Split('.'), out var node) ? node : null;
    }

    /// <summary>Sets one field on a copy of <paramref name="e"/>; null (with <paramref name="why"/>) if it cannot be applied.</summary>
    public static EntityEnvelope? TrySet(EntityEnvelope e, string field, JsonElement value, out string why) =>
        Set(e, field, value, out why);

    /// <summary>The entity's current value at <paramref name="field"/>, or null when the path does not resolve.</summary>
    public static JsonElement? GetValue(EntityEnvelope e, string field)
    {
        var node = GetCurrent(e, field);
        return node is null ? null : JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    /// <summary>
    /// Like <see cref="GetValue"/> but distinguishes an absent path (returns false) from a JSON null
    /// (returns true with a Null-kind element).
    /// </summary>
    public static bool TryGetValue(EntityEnvelope e, string field, out JsonElement value)
    {
        value = default;
        if (field is "name" or "canonicalText")
        {
            value = JsonDocument.Parse(JsonValue.Create(field == "name" ? e.Name : e.CanonicalText)!.ToJsonString()).RootElement.Clone();
            return true;
        }
        if (!field.StartsWith(FieldsPrefix, StringComparison.Ordinal)) return false;
        if (!TryWalk(FieldsNode(e), field[FieldsPrefix.Length..].Split('.'), out var node)) return false;
        value = JsonDocument.Parse(node?.ToJsonString() ?? "null").RootElement.Clone();
        return true;
    }

    /// <summary>Deletes the object key at <paramref name="field"/>; false when the path is not an object key under fields.</summary>
    public static EntityEnvelope? TryRemoveKey(EntityEnvelope e, string field)
    {
        if (!field.StartsWith(FieldsPrefix, StringComparison.Ordinal)) return null;
        var segments = field[FieldsPrefix.Length..].Split('.');
        var root = FieldsNode(e);
        JsonNode? parent = root;
        if (segments.Length > 1 && !TryWalk(root, segments[..^1], out parent)) return e;
        if (parent is not JsonObject obj) return null;
        obj.Remove(segments[^1]);
        using var doc = JsonDocument.Parse(root.ToJsonString());
        return e with { Fields = doc.RootElement.Clone() };
    }

    /// <summary>The drift check's text for an override whose baseline was <paramref name="baseline"/> — shared with <see cref="DriftNow"/>.</summary>
    private static string DriftPrefix(string field, JsonElement baseline) =>
        $"extraction changed under your override at {field}: was {baseline.GetRawText()}, now ";

    /// <summary>
    /// The re-extracted value recorded in an unresolved drift check for <paramref name="ov"/>, or null when the entity
    /// has no such check. A JSON null current value comes back as a JsonElement of kind Null.
    /// </summary>
    public static JsonElement? DriftNow(EntityEnvelope e, FieldOverride ov)
    {
        if (ov.Extracted is not { } baseline) return null;
        var prefix = DriftPrefix(ov.Field, baseline);
        var check = e.Checks.FirstOrDefault(c => c.Name == DriftCheck && c.Reason.StartsWith(prefix, StringComparison.Ordinal));
        return check is null ? null : JsonDocument.Parse(check.Reason[prefix.Length..]).RootElement.Clone();
    }

    private static EntityEnvelope? Set(EntityEnvelope e, string field, JsonElement value, out string why)
    {
        why = "";
        switch (field)
        {
            case "name":
            case "canonicalText":
                if (value.ValueKind != JsonValueKind.String)
                {
                    why = $"value must be a string, got {value.ValueKind}";
                    return null;
                }
                return field == "name"
                    ? e with { Name = value.GetString()! }
                    : e with { CanonicalText = value.GetString()! };
        }
        if (!field.StartsWith(FieldsPrefix, StringComparison.Ordinal))
        {
            why = "unknown field root";
            return null;
        }

        var segments = field[FieldsPrefix.Length..].Split('.');
        if (segments.Any(s => s.Length == 0))
        {
            why = "empty path segment";
            return null;
        }
        var root = FieldsNode(e);
        JsonNode? parent = root;
        if (segments.Length > 1 && !TryWalk(root, segments[..^1], out parent))
        {
            why = "parent path no longer exists after re-extraction";
            return null;
        }
        var last = segments[^1];
        var node = JsonNode.Parse(value.GetRawText());
        switch (parent)
        {
            case JsonObject obj:
                obj[last] = node;
                break;
            case JsonArray arr when int.TryParse(last, out var i) && i >= 0 && i < arr.Count:
                arr[i] = node;
                break;
            default:
                why = "parent is not an object, or the array index is out of range";
                return null;
        }
        using var doc = JsonDocument.Parse(root.ToJsonString());
        return e with { Fields = doc.RootElement.Clone() };
    }

    private static JsonNode FieldsNode(EntityEnvelope e) =>
        e.Fields.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? new JsonObject()
            : JsonNode.Parse(e.Fields.GetRawText()) ?? new JsonObject();

    /// <summary>Walks a path; <c>found</c> is true even when the value is JSON null.</summary>
    private static bool TryWalk(JsonNode? start, string[] segments, out JsonNode? node)
    {
        node = start;
        foreach (var seg in segments)
        {
            switch (node)
            {
                case JsonObject obj when obj.TryGetPropertyValue(seg, out var child):
                    node = child;
                    break;
                case JsonArray arr when int.TryParse(seg, out var i) && i >= 0 && i < arr.Count:
                    node = arr[i];
                    break;
                default:
                    node = null;
                    return false;
            }
        }
        return true;
    }
}
