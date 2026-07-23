using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Weapon"/> — the weapon
/// partition of the base-item split (see <see cref="BaseItemPartition"/>): every mundane
/// items-base.json/items.json element carrying a <c>"weaponCategory"</c> property. Projects a
/// curated <see cref="Domain.Entities.Fields.WeaponFields"/> shape — self-contained, like
/// <see cref="GodBackfillProvider"/>/<see cref="SpellBackfillProvider"/>, NOT the generic
/// field-fill mapper's raw clone.
/// </summary>
public sealed partial class WeaponBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.Weapon;

    [GeneratedRegex(@"^(\d+)d(\d+)$")]
    private static partial Regex DiceExpressionPattern();

    private static readonly Dictionary<string, string> DamageTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["B"] = "bludgeoning",
        ["P"] = "piercing",
        ["S"] = "slashing",
    };

    private static readonly Dictionary<string, string> PropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = "Ammunition",
        ["AF"] = "Ammunition (Firearm)",
        ["BF"] = "Burst Fire (Firearm)",
        ["2H"] = "Two-Handed",
        ["F"] = "Finesse",
        ["H"] = "Heavy",
        ["L"] = "Light",
        ["LD"] = "Loading",
        ["R"] = "Reach",
        ["RLD"] = "Reload",
        ["S"] = "Special",
        ["T"] = "Thrown",
        ["V"] = "Versatile",
    };

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
    /// Builds the canonical Weapon <c>fields</c> shape (see
    /// <see cref="Domain.Entities.Fields.WeaponFields"/>): weapon category
    /// ("simple"/"martial", Titleized), weapon type ("M"/"R" -> Melee/Ranged), cost in copper
    /// pieces, weight in pounds, the primary damage (dice, computed average, and full
    /// damage-type name; <c>dmg2</c>, the versatile two-handed dice, when present), the
    /// normal/long range (thrown/ranged weapons only), and the human-readable property list.
    /// </summary>
    private static JsonElement BuildFields(JsonElement weapon)
    {
        var fields = new JsonObject
        {
            ["category"] = Titleize(StringOrEmpty(weapon, "weaponCategory")),
            ["weaponType"] = GetWeaponType(weapon),
            ["costCp"] = GetCostCp(weapon),
            ["weightLb"] = GetWeightLb(weapon),
            ["damage"] = GetDamage(weapon),
            ["range"] = GetRange(weapon),
            ["properties"] = GetProperties(weapon),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static string GetWeaponType(JsonElement weapon)
    {
        var code = BaseItemPartition.StripSource(StringOrEmpty(weapon, "type"));
        return code switch
        {
            "M" => "Melee",
            "R" => "Ranged",
            _ => code,
        };
    }

    private static int GetCostCp(JsonElement weapon)
        => weapon.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var cp)
            ? cp
            : 0;

    private static double GetWeightLb(JsonElement weapon)
        => weapon.TryGetProperty("weight", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetDouble(out var lb)
            ? lb
            : 0;

    private static JsonObject GetDamage(JsonElement weapon)
    {
        var dice = StringOrEmpty(weapon, "dmg1");
        var typeCode = StringOrEmpty(weapon, "dmgType");
        var damage = new JsonObject
        {
            ["dice"] = dice,
            ["average"] = ComputeAverage(dice),
            ["type"] = DamageTypeNames.TryGetValue(typeCode, out var typeName) ? typeName : typeCode,
        };

        if (weapon.TryGetProperty("dmg2", out var dmg2) && dmg2.ValueKind == JsonValueKind.String)
            damage["versatile"] = dmg2.GetString();

        return damage;
    }

    private static int ComputeAverage(string dice)
    {
        var match = DiceExpressionPattern().Match(dice);
        if (!match.Success) return 0;

        var count = int.Parse(match.Groups[1].Value);
        var sides = int.Parse(match.Groups[2].Value);
        return (int)Math.Floor(count * (sides + 1) / 2.0);
    }

    private static JsonNode? GetRange(JsonElement weapon)
    {
        if (!weapon.TryGetProperty("range", out var r) || r.ValueKind != JsonValueKind.String)
            return null;

        var parts = r.GetString()!.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var normal) || !int.TryParse(parts[1], out var longRange))
            return null;

        return new JsonObject { ["normal"] = normal, ["long"] = longRange };
    }

    private static JsonArray GetProperties(JsonElement weapon)
    {
        var arr = new JsonArray();
        if (weapon.TryGetProperty("property", out var props) && props.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in props.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.String) continue;
                var code = BaseItemPartition.StripSource(p.GetString()!);
                arr.Add(PropertyNames.TryGetValue(code, out var label) ? label : code);
            }
        }
        return arr;
    }

    private static string StringOrEmpty(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    private static string Titleize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}