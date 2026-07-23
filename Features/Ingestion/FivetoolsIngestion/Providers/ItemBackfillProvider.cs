using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Item"/> — the mundane
/// "everything else" partition of the base-item split (see <see cref="BaseItemPartition"/>):
/// every mundane items-base.json/items.json element that is neither a weapon
/// (<see cref="WeaponBackfillProvider"/>) nor armor (<see cref="ArmorBackfillProvider"/>).
/// Projects a curated <see cref="Domain.Entities.Fields.ItemFields"/> shape — self-contained,
/// like <see cref="GodBackfillProvider"/>/<see cref="SpellBackfillProvider"/>, NOT the generic
/// field-fill mapper's raw clone.
/// </summary>
public sealed class ItemBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.Item;

    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
        => BaseItemPartition.EnumerateRoster(fivetoolsDir, BaseItemPartition.Kind.Item);

    public EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element)
    {
        int? page = element.TryGetProperty("page", out var pg) && pg.TryGetInt32(out var pv) ? pv : null;
        var srd = element.TryGetProperty("srd", out var s) && s.ValueKind == JsonValueKind.True;
        var srd52 = element.TryGetProperty("srd52", out var s2) && s2.ValueKind == JsonValueKind.True;

        return new EntityEnvelope(
            Id: EntityIdSlug.For(sourceKey, EntityType.Item, name),
            Type: EntityType.Item,
            Name: name,
            SourceBook: sourceKey,
            Edition: edition,
            Page: page,
            FirstAppearedIn: new FirstAppearance(sourceKey, edition, page),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: BuildFields(element),
            DataSource: "5etools-backfill",
            Srd: srd,
            Srd52: srd52,
            BasicRules2024: false,
            NeedsReview: false,
            Keywords: Array.Empty<string>(),
            Disposition: EntityDisposition.Accepted);
    }

    /// <summary>
    /// Builds the canonical Item <c>fields</c> shape (see
    /// <see cref="Domain.Entities.Fields.ItemFields"/>): cost in copper pieces (5etools'
    /// <c>value</c> is already denominated in cp), weight in pounds, and a description assembled
    /// from <c>entries[]</c> (falling back to <c>additionalEntries[]</c> for base items that
    /// only carry rules text there, e.g. tool-proficiency gear).
    /// </summary>
    private static JsonElement BuildFields(JsonElement item)
    {
        var fields = new JsonObject
        {
            ["costCp"] = GetCostCp(item),
            ["weightLb"] = GetWeightLb(item),
            ["description"] = GetDescription(item),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static int GetCostCp(JsonElement item)
        => item.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var cp)
            ? cp
            : 0;

    private static double GetWeightLb(JsonElement item)
        => item.TryGetProperty("weight", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetDouble(out var lb)
            ? lb
            : 0;

    private static string GetDescription(JsonElement item)
    {
        if (item.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            var text = FivetoolsEntryText.Flatten(entries);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        if (item.TryGetProperty("additionalEntries", out var addl) && addl.ValueKind == JsonValueKind.Array)
            return FivetoolsEntryText.Flatten(addl);

        return "";
    }
}