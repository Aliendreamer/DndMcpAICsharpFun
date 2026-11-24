using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

public sealed record MergeResult(IReadOnlyList<EntityEnvelope> Entities, IReadOnlyList<string> OrphanedEntityIds, IReadOnlyList<FieldOverride> Retired);

/// <summary>Re-applies admin overrides over freshly extracted entities and derives each entity's disposition.</summary>
public static class OverrideMerger
{
    public const string DriftCheck = "override-drift";
    public const string UnappliedCheck = "override-unapplied";
    private const string FieldsPrefix = "fields.";

    /// <summary>
    /// Re-applies the overrides and approvals to entities that already hold an earlier application (the current canonical
    /// file, no re-extraction): an override whose value is already in place is left alone, and stale
    /// drift / unapplied checks are dropped and re-derived.
    /// </summary>
    public static MergeResult Reapply(IReadOnlyList<EntityEnvelope> current, EntityOverrides o) => Apply(current, o, reapply: true);

    public static MergeResult Apply(IReadOnlyList<EntityEnvelope> extracted, EntityOverrides o) => Apply(extracted, o, reapply: false);

    private static MergeResult Apply(IReadOnlyList<EntityEnvelope> extracted, EntityOverrides o, bool reapply)
    {
        // Rescue entities are derived, never input: drop any the canonical file already holds and recompute them.
        // A fresh extraction never carries rescues, so a real entity whose id merely ends in "-trimmed" is kept there.
        var inputs = reapply ? extracted.Where(e => !TrimRescue.IsRescueId(e.Id)).ToList() : extracted.ToList();

        var result = new List<EntityEnvelope>(inputs.Count);
        var retired = new List<FieldOverride>();
        var trimmedHosts = new List<(EntityEnvelope Host, string BaseText)>();

        EntityEnvelope Process(EntityEnvelope input)
        {
            var original = input;
            var mine = o.Overrides.Where(x => x.EntityId == original.Id).ToList();
            var approval = ApprovalFor(o, original.Id);
            if (reapply && mine.Count > 0)
                original = original with
                {
                    Checks = original.Checks.Where(c => c.Name is not (DriftCheck or UnappliedCheck)).ToList(),
                };
            var e = original;
            // In file order: for the same field the last override wins.
            foreach (var ov in mine)
                e = ApplyOne(original, e, ov, reapply, retired);

            // Programmatic entities always get a derived disposition (never trust whatever the input carried).
            if (e.DataSource == DataSources.Programmatic || mine.Count > 0 || approval is not null || e.Checks.Count > 0 || IsDeclined(o, original.Id))
                e = SetDisposition(e, approval, IsDeclined(o, original.Id));
            return e;
        }

        foreach (var input in inputs)
        {
            var textOverrides = o.Overrides.Where(x => x.EntityId == input.Id && x.Field == "canonicalText").ToList();
            if (textOverrides.Count > 0)
            {
                // Fresh extraction: the extracted text is the base. Reapply: the text recorded when the override was made.
                // After drift the recorded text is stale: the drift check holds what the extractor produced then.
                var baseText = reapply
                    ? textOverrides
                        .Select(x => DriftNow(input, x) ?? x.Extracted)
                        .LastOrDefault(x => x is { ValueKind: JsonValueKind.String })?.GetString()
                    : WithRecordedLines(input.CanonicalText,
                        textOverrides.Select(x => x.Extracted)
                            .LastOrDefault(x => x is { ValueKind: JsonValueKind.String })?.GetString());
                if (baseText is not null)
                    trimmedHosts.Add((input, baseText));
            }
            result.Add(Process(input));
        }

        foreach (var rescue in TrimRescue.Compute(trimmedHosts, result))
            result.Add(Process(rescue));

        var ids = result.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var orphans = o.Overrides.Select(x => x.EntityId)
            .Concat(o.Approvals.Select(a => a.EntityId))
            .Concat(o.Declines.Select(d => d.EntityId))
            .Where(id => !ids.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new MergeResult(result, orphans, retired);
    }

    /// <summary>The entity's approval, if any (the last one wins).</summary>
    /// <summary>
    /// The new extracted text plus the lines of the text recorded with the override that it lacks, so a retired override
    /// (the extractor now yields the override's value) does not erase the text it once trimmed.
    /// </summary>
    private static string WithRecordedLines(string extractedText, string? recorded)
    {
        if (recorded is null) return extractedText;
        var have = extractedText.Split('\n').Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);
        var extra = recorded.Split('\n').Where(l => !have.Contains(l.Trim())).ToList();
        return extra.Count == 0 ? extractedText : extractedText + "\n" + string.Join("\n", extra);
    }

    public static Approval? ApprovalFor(EntityOverrides o, string entityId) =>
        o.Approvals.LastOrDefault(a => a.EntityId == entityId);

    /// <summary>Override drift and unapplied overrides can never be waived by an approval.</summary>
    public static bool IsWaivable(string checkName) => checkName is not (DriftCheck or UnappliedCheck);

    /// <summary>The names of the entity's currently failing checks that an approval may waive.</summary>
    public static IReadOnlyList<string> WaivableFailedChecks(EntityEnvelope e) =>
        e.Checks.Where(c => !c.Passed && IsWaivable(c.Name)).Select(c => c.Name).Distinct(StringComparer.Ordinal).ToList();

    public const string EntryLevelCheck = "entry-level";
    public const string NameWordsKnownCheck = "name-words-known";

    /// <summary>
    /// A failed <c>entry-level</c> check is advisory: alone, or with only a failed <c>name-words-known</c>, it does not block.
    /// Page checks of 74 such entities found about 1 in 5 with a real boundary error (openspec entry-level-advisory).
    /// </summary>
    private static bool IsAdvisoryOnly(IReadOnlyList<string> open) =>
        open.Contains(EntryLevelCheck, StringComparer.Ordinal)
        && open.All(n => n is EntryLevelCheck or NameWordsKnownCheck);

    /// <summary>True when the overrides file records a decline for the entity.</summary>
    public static bool IsDeclined(EntityOverrides o, string entityId) =>
        o.Declines.Any(d => d.EntityId == entityId);

    public static EntityEnvelope SetDisposition(EntityEnvelope e, Approval? approval, bool declined = false)
    {
        // A recorded decline outranks every check and any approval.
        if (declined)
            return e with { Disposition = EntityDisposition.Declined, NeedsReview = false };
        // An approval waives only the failed checks it recorded at approval time (sticky, per check name),
        // and never an unresolved override drift or an override that could not be applied.
        if (LeadInLore.Is(e))
            return e with { Disposition = EntityDisposition.Declined, NeedsReview = false };
        var waived = approval?.WaivedChecks ?? [];
        var open = e.Checks
            .Where(c => !c.Passed && (!IsWaivable(c.Name) || !waived.Contains(c.Name, StringComparer.Ordinal)))
            .Select(c => c.Name)
            .ToList();
        var blocked = open.Count > 0 && !IsAdvisoryOnly(open);
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
    /// Outcome order on a fresh extraction (new = extracted value, old = recorded baseline, want = override value):
    /// (1) <c>TextMatch.Matches(new, want)</c> retires the override; (2) new matches old (normalised) applies it with no drift;
    /// (3) new is closer to want than old (token F1) applies it with no drift; (4) otherwise it applies with override-drift.
    /// An absent new value is never retired and never closer. <c>Reapply</c> keeps the strict <see cref="JsonNode.DeepEquals"/>
    /// comparison (key order ignored, array order matters, numbers by value).
    /// </summary>
    private static EntityEnvelope ApplyOne(EntityEnvelope original, EntityEnvelope e, FieldOverride ov, bool reapply, List<FieldOverride> retired)
    {
        // Drift is judged against the original extraction, not an earlier override on the same field.
        var current = GetCurrent(original, ov.Field);
        // Re-applying onto a file that already holds the override: nothing to set, and no drift to report.
        if (reapply && TryGetValue(e, ov.Field, out var held)
            && JsonNode.DeepEquals(JsonNode.Parse(held.GetRawText()), JsonNode.Parse(ov.Value.GetRawText())))
            return e;
        // Fresh extraction: an override the extractor now reproduces is retired (the entity keeps the extracted value).
        if (!reapply && current is not null && JsonNode.Parse(ov.Value.GetRawText()) is { } wanted && TextMatch.Matches(current, wanted))
        {
            retired.Add(ov);
            return e;
        }
        // A correction that removes a field is moot once the extractor no longer produces it.
        if (!reapply && current is null && ov.Value.ValueKind == JsonValueKind.Null)
        {
            retired.Add(ov);
            return e;
        }
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
            && !JsonNode.DeepEquals(JsonNode.Parse(baseline.GetRawText()), current)
            && (reapply
                || !(current is not null && TextMatch.Matches(current, JsonNode.Parse(baseline.GetRawText())))
                    && !ExtractionGotCloser(current, baseline, ov.Value)))
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

    /// <summary>True when the re-extracted value is closer (token F1) to the override's value than the recorded baseline was.</summary>
    private static bool ExtractionGotCloser(JsonNode? now, JsonElement baseline, JsonElement want)
    {
        if (now is null) return false;
        var wantText = TextMatch.FlattenText(JsonNode.Parse(want.GetRawText()));
        var before = TextMatch.TokenF1(TextMatch.FlattenText(JsonNode.Parse(baseline.GetRawText())), wantText);
        return TextMatch.TokenF1(TextMatch.FlattenText(now), wantText) > before;
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
