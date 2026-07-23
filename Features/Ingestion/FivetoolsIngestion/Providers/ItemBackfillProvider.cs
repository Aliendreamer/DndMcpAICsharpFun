using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Item"/> — the mundane
/// "everything else" partition of the base-item split (see <see cref="BaseItemPartition"/>):
/// every mundane items-base.json/items.json element that is neither a weapon
/// (<see cref="WeaponBackfillProvider"/>) nor armor (<see cref="ArmorBackfillProvider"/>).
/// Projects the RAW <c>fields</c> shape the
/// <see cref="Features.Entities.CanonicalText.ItemCanonicalTextRenderer"/> reads (<c>type</c> +
/// <c>value</c> + <c>entries</c>) — NOT a curated domain-record shape.
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
    /// Builds the raw <c>fields</c> shape the
    /// <see cref="Features.Entities.CanonicalText.ItemCanonicalTextRenderer"/> reads (and
    /// <c>Schemas/canonical/ItemFields.schema.json</c> describes): the raw <c>type</c> code and
    /// <c>value</c> (cost in copper pieces) copied verbatim, plus a flattened <c>entries</c>
    /// array (falling back to <c>additionalEntries</c> for base items whose rules text only
    /// lives there, e.g. tool-proficiency gear).
    /// </summary>
    private static JsonElement BuildFields(JsonElement item)
    {
        var fields = new JsonObject
        {
            ["type"] = RawFieldCopy.StringOrNull(item, "type"),
            ["entries"] = GetEntries(item),
        };

        // Omit "value" entirely rather than writing a JSON null — some base items (e.g. Sling
        // Bullet, Trinket) carry no cost in 5etools, and ItemCanonicalTextRenderer treats a
        // present-but-null "value" the same as absent (no Value line rendered either way).
        var value = RawFieldCopy.IntOrNull(item, "value");
        if (value.HasValue) fields["value"] = value.Value;

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static JsonArray GetEntries(JsonElement item)
    {
        var entries = FivetoolsEntryText.ToRendererEntries(item);
        if (entries.Count > 0) return entries;

        return FivetoolsEntryText.ToRendererEntries(item, "additionalEntries");
    }
}