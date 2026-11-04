using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

public sealed record MergeResult(IReadOnlyList<EntityEnvelope> Entities, IReadOnlyList<string> OrphanedEntityIds);

/// <summary>Re-applies admin overrides over freshly extracted entities and derives each entity's disposition.</summary>
public static class OverrideMerger
{
    public const string DriftCheck = "override-drift";
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
        foreach (var entity in extracted)
        {
            var mine = o.Overrides.Where(x => x.EntityId == entity.Id).ToList();
            var approved = o.Approvals.Any(a => a.EntityId == entity.Id);
            var e = entity;
            foreach (var ov in mine)
                e = ApplyOne(e, ov);

            if (mine.Count > 0 || approved || e.Checks.Count > 0)
                e = SetDisposition(e, approved);
            result.Add(e);
        }
        return new MergeResult(result, orphans);
    }

    public static EntityEnvelope SetDisposition(EntityEnvelope e, bool approved)
    {
        // An approval waives ordinary failed checks but never an unresolved override drift.
        var blocked = e.Checks.Any(c => !c.Passed && (!approved || c.Name == DriftCheck));
        return blocked
            ? e with { Disposition = EntityDisposition.NeedsReview, NeedsReview = true }
            : e with { Disposition = EntityDisposition.Accepted, NeedsReview = false };
    }

    public static bool ValidatePath(EntityEnvelope e, string field)
    {
        if (field is "name" or "canonicalText") return true;
        if (!field.StartsWith(FieldsPrefix, StringComparison.Ordinal)) return false;
        var root = FieldsNode(e);
        return Walk(root, field[FieldsPrefix.Length..].Split('.')) is not null;
    }

    private static EntityEnvelope ApplyOne(EntityEnvelope e, FieldOverride ov)
    {
        var current = GetCurrent(e, ov.Field);
        var updated = Set(e, ov.Field, ov.Value);
        if (updated is null) return e;

        if (ov.Extracted is { } baseline && !JsonNode.DeepEquals(JsonNode.Parse(baseline.GetRawText()), current))
        {
            var was = baseline.GetRawText();
            var now = current?.ToJsonString() ?? "null";
            updated = updated with
            {
                Checks = [.. updated.Checks.Where(c => c.Name != DriftCheck || c.Reason.Contains(ov.Field) == false),
                    new EntityCheck(DriftCheck, false,
                        $"extraction changed under your override: was {was}, now {now}")],
            };
        }
        return updated;
    }

    private static JsonNode? GetCurrent(EntityEnvelope e, string field) => field switch
    {
        "name" => JsonValue.Create(e.Name),
        "canonicalText" => JsonValue.Create(e.CanonicalText),
        _ when field.StartsWith(FieldsPrefix, StringComparison.Ordinal) =>
            Walk(FieldsNode(e), field[FieldsPrefix.Length..].Split('.')),
        _ => null,
    };

    private static EntityEnvelope? Set(EntityEnvelope e, string field, JsonElement value)
    {
        switch (field)
        {
            case "name":
                return value.ValueKind == JsonValueKind.String ? e with { Name = value.GetString()! } : null;
            case "canonicalText":
                return value.ValueKind == JsonValueKind.String ? e with { CanonicalText = value.GetString()! } : null;
        }
        if (!field.StartsWith(FieldsPrefix, StringComparison.Ordinal)) return null;

        var segments = field[FieldsPrefix.Length..].Split('.');
        var root = FieldsNode(e);
        var parent = segments.Length == 1 ? root : Walk(root, segments[..^1]);
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
                return null;
        }
        using var doc = JsonDocument.Parse(root.ToJsonString());
        return e with { Fields = doc.RootElement.Clone() };
    }

    private static JsonNode FieldsNode(EntityEnvelope e) =>
        e.Fields.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? new JsonObject()
            : JsonNode.Parse(e.Fields.GetRawText()) ?? new JsonObject();

    private static JsonNode? Walk(JsonNode? node, string[] segments)
    {
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
                    return null;
            }
        }
        return node;
    }
}
