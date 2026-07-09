using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.MagicItem"/>. Reads
/// <c>items.json</c>'s <c>"item"</c> array, filtering out mundane items (rarity absent or
/// <c>"none"</c>), and projects a curated <see cref="Domain.Entities.Fields.MagicItemFields"/>
/// shape — a NEW projection (the generic mapper's Clone doesn't match this canonical shape).
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
    /// Builds the canonical MagicItem <c>fields</c> shape (see
    /// <see cref="Domain.Entities.Fields.MagicItemFields"/>): rarity, item category (the
    /// <c>type</c> code with any trailing <c>|SOURCE</c> stripped), attunement requirement, and a
    /// description assembled from the string entries of <c>entries[]</c>.
    /// </summary>
    private static JsonElement BuildFields(JsonElement item)
    {
        var fields = new JsonObject
        {
            ["rarity"] = GetRarity(item),
            ["itemCategory"] = GetItemCategory(item),
            ["attunement"] = GetAttunement(item),
            ["description"] = GetDescription(item),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static string GetRarity(JsonElement item)
        => item.TryGetProperty("rarity", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : "";

    /// <summary>Strips any trailing <c>"|SOURCE"</c> suffix from the 5etools <c>type</c> code, e.g.
    /// <c>"RD|DMG"</c> → <c>"RD"</c>.</summary>
    private static string GetItemCategory(JsonElement item)
    {
        if (!item.TryGetProperty("type", out var v) || v.ValueKind != JsonValueKind.String)
            return "";
        var type = v.GetString()!;
        var pipe = type.IndexOf('|');
        return pipe >= 0 ? type[..pipe] : type;
    }

    private static string GetAttunement(JsonElement item)
    {
        if (!item.TryGetProperty("reqAttune", out var v))
            return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString()!,
            JsonValueKind.True => "requires attunement",
            _ => "",
        };
    }

    private static string GetDescription(JsonElement item)
    {
        if (!item.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return "";
        return FivetoolsEntryText.Flatten(entries);
    }
}