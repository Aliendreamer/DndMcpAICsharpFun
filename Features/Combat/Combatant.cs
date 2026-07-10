using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DndMcpAICsharpFun.Features.Combat;

/// <summary>
/// One participant in a <see cref="Combat"/>. <see cref="HeroId"/> is set only when the combatant
/// was drafted from a campaign hero (enabling DM-approved HP write-back). Conditions persist as a
/// JSON array of enum names in <see cref="ConditionsJson"/>; the <see cref="Conditions"/> helper is
/// not mapped. Storing a scalar JSON string (rather than a value-converted collection) avoids the
/// EF collection-comparer warning that warnings-as-errors would turn into a build failure.
/// </summary>
public sealed class Combatant
{
    public long Id { get; set; }
    public long CombatId { get; set; }
    public long? HeroId { get; set; }
    public string Name { get; set; } = "";
    public bool IsPlayer { get; set; }
    public int? InitiativeRoll { get; set; }
    public int InitiativeModifier { get; set; }
    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }
    public int? Ac { get; set; }
    public string ConditionsJson { get; set; } = "[]";
    public int AddedOrder { get; set; }

    [NotMapped]
    public IReadOnlyList<Condition> Conditions
    {
        get => CombatantConditions.Deserialize(ConditionsJson);
        set => ConditionsJson = CombatantConditions.Serialize(value);
    }
}

/// <summary>Serializes a combatant's condition set as a JSON array of enum names.</summary>
public static class CombatantConditions
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IReadOnlyList<Condition> conditions) =>
        JsonSerializer.Serialize(conditions, Options);

    public static IReadOnlyList<Condition> Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<Condition>>(json, Options) ?? [];
}
