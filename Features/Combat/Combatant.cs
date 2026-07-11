using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DndMcpAICsharpFun.Features.Combat;

/// <summary>
/// One participant in a <see cref="Combat"/>. <see cref="HeroId"/> is set only when the combatant
/// was drafted from a campaign hero (enabling DM-approved HP write-back). Conditions persist as a
/// JSON array of <see cref="ConditionTimer"/> objects in <see cref="ConditionsJson"/> (legacy rows
/// written as a plain array of enum names are still readable); the <see cref="Conditions"/> helper
/// is not mapped. Storing a scalar JSON string (rather than a value-converted collection) avoids the
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
    public IReadOnlyList<ConditionTimer> Conditions
    {
        get => CombatantConditions.Deserialize(ConditionsJson);
        set => ConditionsJson = CombatantConditions.Serialize(value);
    }
}

/// <summary>A condition applied to a combatant; null <see cref="RoundsRemaining"/> means indefinite.</summary>
public sealed record ConditionTimer(Condition Condition, int? RoundsRemaining = null);

/// <summary>
/// Serializes a combatant's condition set as a JSON array of objects (<see cref="ConditionTimer"/>).
/// Also reads the legacy shape (a JSON array of enum names) as indefinite timers, for backward
/// compatibility with data written before rounds-based durations existed.
/// </summary>
public static class CombatantConditions
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IReadOnlyList<ConditionTimer> conditions) =>
        JsonSerializer.Serialize(conditions, Options);

    public static IReadOnlyList<ConditionTimer> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

        var firstKind = JsonValueKind.Undefined;
        foreach (var el in doc.RootElement.EnumerateArray()) { firstKind = el.ValueKind; break; }

        // Legacy shape: a JSON array of enum names → indefinite timers.
        if (firstKind == JsonValueKind.String)
            return (JsonSerializer.Deserialize<List<Condition>>(json, Options) ?? [])
                .Select(c => new ConditionTimer(c, null)).ToList();

        // New shape (or empty): array of { Condition, RoundsRemaining }.
        return JsonSerializer.Deserialize<List<ConditionTimer>>(json, Options) ?? [];
    }
}
