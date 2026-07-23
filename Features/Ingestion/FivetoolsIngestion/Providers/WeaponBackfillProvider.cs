using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Weapon"/> — the weapon
/// partition of the base-item split (see <see cref="BaseItemPartition"/>): every mundane
/// items-base.json/items.json element carrying a <c>"weaponCategory"</c> property. Projects the
/// RAW <c>fields</c> shape the <see cref="Features.Entities.CanonicalText.WeaponCanonicalTextRenderer"/>
/// reads (<c>weaponCategory</c>/<c>dmg1</c>/<c>dmgType</c> + <c>entries</c>) — NOT a curated
/// domain-record shape.
/// </summary>
public sealed class WeaponBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.Weapon;

    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
        => BaseItemPartition.EnumerateRoster(fivetoolsDir, BaseItemPartition.Kind.Weapon);

    public EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element)
    {
        int? page = element.TryGetProperty("page", out var pg) && pg.TryGetInt32(out var pv) ? pv : null;
        var srd = element.TryGetProperty("srd", out var s) && s.ValueKind == JsonValueKind.True;
        var srd52 = element.TryGetProperty("srd52", out var s2) && s2.ValueKind == JsonValueKind.True;

        return new EntityEnvelope(
            Id: EntityIdSlug.For(sourceKey, EntityType.Weapon, name),
            Type: EntityType.Weapon,
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
    /// <see cref="Features.Entities.CanonicalText.WeaponCanonicalTextRenderer"/> reads (and
    /// <c>Schemas/canonical/WeaponFields.schema.json</c> describes): the raw
    /// <c>weaponCategory</c>/<c>dmg1</c>/<c>dmgType</c> codes copied verbatim, plus a flattened
    /// <c>entries</c> array.
    /// </summary>
    private static JsonElement BuildFields(JsonElement weapon)
    {
        var fields = new JsonObject
        {
            ["weaponCategory"] = RawFieldCopy.StringOrNull(weapon, "weaponCategory"),
            ["dmg1"] = RawFieldCopy.StringOrNull(weapon, "dmg1"),
            ["dmgType"] = RawFieldCopy.StringOrNull(weapon, "dmgType"),
            ["entries"] = FivetoolsEntryText.ToRendererEntries(weapon),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }
}