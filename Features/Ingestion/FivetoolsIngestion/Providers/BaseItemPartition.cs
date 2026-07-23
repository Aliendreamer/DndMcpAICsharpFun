using System.Collections.Frozen;
using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// The ONE partition definition shared by <see cref="ItemBackfillProvider"/>,
/// <see cref="WeaponBackfillProvider"/>, and <see cref="ArmorBackfillProvider"/> so a mundane
/// base item is claimed by exactly one of the three. Classification mirrors the existing
/// field-fill mappers (<see cref="Mappers.FivetoolsWeaponMapper"/>,
/// <see cref="Mappers.FivetoolsArmorMapper"/>): a <c>"weaponCategory"</c> property means Weapon;
/// a <c>"type"</c> code of LA/MA/HA/S (armor — any <c>"|SOURCE"</c> suffix stripped, e.g.
/// <c>"LA|XPHB"</c> -> <c>"LA"</c>) means Armor; everything else mundane is Item. Rarity-present
/// items (magic items, handled entirely separately by <see cref="MagicItemBackfillProvider"/>)
/// are excluded via <see cref="IsMundane"/> — the exact complement of that provider's
/// rarity-present filter — so none of the three ever double-counts a magic item.
/// </summary>
public static class BaseItemPartition
{
    public enum Kind { Weapon, Armor, Item }

    private static readonly FrozenSet<string> ArmorTypeCodes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LA", "MA", "HA", "S" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the item is NOT a magic item: rarity absent, or rarity == "none".</summary>
    public static bool IsMundane(JsonElement item)
        => !item.TryGetProperty("rarity", out var r)
            || r.ValueKind != JsonValueKind.String
            || string.Equals(r.GetString(), "none", StringComparison.OrdinalIgnoreCase);

    public static Kind Classify(JsonElement item)
    {
        if (item.TryGetProperty("weaponCategory", out var wc) && wc.ValueKind == JsonValueKind.String)
            return Kind.Weapon;

        if (item.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            && ArmorTypeCodes.Contains(StripSource(t.GetString()!)))
            return Kind.Armor;

        return Kind.Item;
    }

    /// <summary>Strips a trailing <c>"|SOURCE"</c> suffix from a 5etools type code, e.g.
    /// <c>"LA|XPHB"</c> -> <c>"LA"</c>.</summary>
    public static string StripSource(string code)
    {
        var pipe = code.IndexOf('|');
        return pipe >= 0 ? code[..pipe] : code;
    }

    /// <summary>Enumerates every mundane (non-magic) element of the given <paramref name="kind"/>
    /// across items-base.json's "baseitem" array and items.json's mundane "item" entries.</summary>
    public static IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir, Kind kind)
    {
        foreach (var el in ReadArray(fivetoolsDir, "items-base.json", "baseitem"))
            if (IsMundane(el) && Classify(el) == kind)
                yield return el;

        foreach (var el in ReadArray(fivetoolsDir, "items.json", "item"))
            if (IsMundane(el) && Classify(el) == kind)
                yield return el;
    }

    private static IEnumerable<JsonElement> ReadArray(string fivetoolsDir, string fileName, string arrayKey)
    {
        var path = Path.Combine(fivetoolsDir, fileName);
        if (!File.Exists(path)) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty(arrayKey, out var arr) || arr.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var el in arr.EnumerateArray())
                yield return el.Clone();
        }
    }
}