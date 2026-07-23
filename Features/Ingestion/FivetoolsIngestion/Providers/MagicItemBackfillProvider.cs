using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.MagicItem"/>. Reads
/// <c>items.json</c>'s <c>"item"</c> array, filtering out mundane items (rarity absent or
/// <c>"none"</c>), and projects the RAW <c>fields</c> shape the
/// <see cref="Features.Entities.CanonicalText.MagicItemCanonicalTextRenderer"/> reads
/// (<c>rarity</c>/<c>type</c>/<c>reqAttune</c> + <c>entries</c>) — NOT a curated domain-record
/// shape.
/// </summary>
public sealed class MagicItemBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.MagicItem;

    /// <summary>Raw 5etools item records from items.json's "item" array, PLUS synthetic templated
    /// +N variants expanded from magicvariants.json against the items-base.json base-item pool (see
    /// <see cref="MagicVariantExpander"/>) — both filtered to magic items only (rarity present and
    /// not "none" — this is the magic-item filter).</summary>
    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
    {
        var path = Path.Combine(fivetoolsDir, "items.json");
        if (File.Exists(path))
        {
            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
            catch (JsonException) { /* fall through: skip items.json, still try variants below */ }

            if (doc is not null)
            {
                using (doc)
                {
                    if (doc.RootElement.TryGetProperty("item", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in arr.EnumerateArray())
                        {
                            if (IsQualifyingRarity(el))
                                yield return el.Clone();
                        }
                    }
                }
            }
        }

        // Templated +N variants (e.g. "+1 Longsword") live in magicvariants.json, not items.json —
        // expand them against the base-item pool so recall/backfill see them too.
        foreach (var el in MagicVariantExpander.Expand(fivetoolsDir))
        {
            if (IsQualifyingRarity(el))
                yield return el;
        }
    }

    private static bool IsQualifyingRarity(JsonElement el)
        => el.TryGetProperty("rarity", out var rarity)
            && rarity.ValueKind == JsonValueKind.String
            && !string.Equals(rarity.GetString(), "none", StringComparison.OrdinalIgnoreCase);

    public EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element)
    {
        int? page = element.TryGetProperty("page", out var pg) && pg.TryGetInt32(out var pv) ? pv : null;
        var srd = element.TryGetProperty("srd", out var s) && s.ValueKind == JsonValueKind.True;
        var srd52 = element.TryGetProperty("srd52", out var s2) && s2.ValueKind == JsonValueKind.True;

        return new EntityEnvelope(
            Id: EntityIdSlug.For(sourceKey, EntityType.MagicItem, name),
            Type: EntityType.MagicItem,
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
    /// <see cref="Features.Entities.CanonicalText.MagicItemCanonicalTextRenderer"/> reads (and
    /// <c>Schemas/canonical/MagicItemFields.schema.json</c> describes): <c>rarity</c>/<c>type</c>
    /// strings and <c>reqAttune</c> (bool OR string, copied verbatim — the renderer only checks
    /// it's neither <c>false</c> nor <c>null</c>) copied straight from the source, plus a
    /// flattened <c>entries</c> array.
    /// </summary>
    private static JsonElement BuildFields(JsonElement item)
    {
        var fields = new JsonObject
        {
            ["rarity"] = RawFieldCopy.StringOrNull(item, "rarity"),
            ["type"] = RawFieldCopy.StringOrNull(item, "type"),
            ["reqAttune"] = RawFieldCopy.Any(item, "reqAttune"),
            ["entries"] = FivetoolsEntryText.ToRendererEntries(item),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }
}