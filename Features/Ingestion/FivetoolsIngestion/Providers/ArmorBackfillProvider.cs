using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Armor"/> — the armor
/// partition of the base-item split (see <see cref="BaseItemPartition"/>): every mundane
/// items-base.json/items.json element whose <c>"type"</c> code (source suffix stripped) is
/// LA/MA/HA/S. Projects the RAW <c>fields</c> shape the
/// <see cref="Features.Entities.CanonicalText.ArmorCanonicalTextRenderer"/> reads (<c>type</c> +
/// <c>ac</c> + <c>entries</c>) — NOT a curated domain-record shape.
/// </summary>
public sealed class ArmorBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.Armor;

    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
        => BaseItemPartition.EnumerateRoster(fivetoolsDir, BaseItemPartition.Kind.Armor);

    public EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element)
    {
        int? page = element.TryGetProperty("page", out var pg) && pg.TryGetInt32(out var pv) ? pv : null;
        var srd = element.TryGetProperty("srd", out var s) && s.ValueKind == JsonValueKind.True;
        var srd52 = element.TryGetProperty("srd52", out var s2) && s2.ValueKind == JsonValueKind.True;

        return new EntityEnvelope(
            Id: EntityIdSlug.For(sourceKey, EntityType.Armor, name),
            Type: EntityType.Armor,
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
    /// <see cref="Features.Entities.CanonicalText.ArmorCanonicalTextRenderer"/> reads (and
    /// <c>Schemas/canonical/ArmorFields.schema.json</c> describes): the raw <c>type</c> code and
    /// <c>ac</c> number copied verbatim, plus a flattened <c>entries</c> array.
    /// </summary>
    private static JsonElement BuildFields(JsonElement armor)
    {
        var fields = new JsonObject
        {
            ["type"] = RawFieldCopy.StringOrNull(armor, "type"),
            ["entries"] = FivetoolsEntryText.ToRendererEntries(armor),
        };

        // Omit "ac" entirely rather than writing a JSON null — ArmorCanonicalTextRenderer
        // treats a present-but-null "ac" the same as absent (no AC line rendered either way).
        var ac = RawFieldCopy.IntOrNull(armor, "ac");
        if (ac.HasValue) fields["ac"] = ac.Value;

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }
}