using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Armor"/> — the armor
/// partition of the base-item split (see <see cref="BaseItemPartition"/>): every mundane
/// items-base.json/items.json element whose <c>"type"</c> code (source suffix stripped) is
/// LA/MA/HA/S. Projects a curated <see cref="Domain.Entities.Fields.ArmorFields"/> shape —
/// self-contained, like <see cref="GodBackfillProvider"/>/<see cref="SpellBackfillProvider"/>,
/// NOT the generic field-fill mapper's raw clone.
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
    /// Builds the canonical Armor <c>fields</c> shape (see
    /// <see cref="Domain.Entities.Fields.ArmorFields"/>): category (Light/Medium/Heavy/Shield
    /// from the type code), cost in gold pieces (5etools' <c>value</c> is in cp; divided by
    /// 100), weight in pounds, an AC formula string appropriate to the category (shields are a
    /// flat bonus; light/medium armor add a Dex modifier, medium capped at +2; heavy is fixed),
    /// the Strength requirement (when present), and the stealth-disadvantage flag.
    /// </summary>
    private static JsonElement BuildFields(JsonElement armor)
    {
        var (category, code) = GetCategory(armor);
        var ac = armor.TryGetProperty("ac", out var acEl) && acEl.ValueKind == JsonValueKind.Number && acEl.TryGetInt32(out var acv)
            ? acv
            : 10;

        var fields = new JsonObject
        {
            ["category"] = category,
            ["costGp"] = GetCostGp(armor),
            ["weightLb"] = GetWeightLb(armor),
            ["acFormula"] = BuildAcFormula(code, ac),
            ["strengthRequirement"] = GetStrengthRequirement(armor),
            ["stealthDisadvantage"] = armor.TryGetProperty("stealth", out var st) && st.ValueKind == JsonValueKind.True,
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static (string Category, string Code) GetCategory(JsonElement armor)
    {
        var code = armor.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? BaseItemPartition.StripSource(t.GetString()!)
            : "";

        return code.ToUpperInvariant() switch
        {
            "LA" => ("Light", code),
            "MA" => ("Medium", code),
            "HA" => ("Heavy", code),
            "S" => ("Shield", code),
            _ => ("", code),
        };
    }

    private static string BuildAcFormula(string code, int ac) => code.ToUpperInvariant() switch
    {
        "LA" => $"{ac} + Dex modifier",
        "MA" => $"{ac} + Dex modifier (max 2)",
        "HA" => $"{ac}",
        "S" => $"+{ac}",
        _ => $"{ac}",
    };

    private static int GetCostGp(JsonElement armor)
        => armor.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var cp)
            ? cp / 100
            : 0;

    private static double GetWeightLb(JsonElement armor)
        => armor.TryGetProperty("weight", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetDouble(out var lb)
            ? lb
            : 0;

    private static int? GetStrengthRequirement(JsonElement armor)
        => armor.TryGetProperty("strength", out var s) && s.ValueKind == JsonValueKind.String
            && int.TryParse(s.GetString(), out var str)
            ? str
            : null;
}