namespace DndMcpAICsharpFun.Features.Encounters;

/// <summary>A caller-supplied monster name plus how many of it to include (rate side).</summary>
public sealed record MonsterQuantity(string Name, int Quantity);

/// <summary>A monster and how many copies of it appear in a flat encounter list (display side).</summary>
public sealed record MonsterCount(MonsterRef Monster, int Count);

/// <summary>
/// Presentation-only grouping of a flat <see cref="MonsterRef"/> list (in which quantity is
/// expressed as repeated entries) into per-id counts, so chat text and the encounter panel can
/// render "8× Goblin" without the assessed set ever leaving its flat, per-combatant form.
/// </summary>
public static class MonsterGrouping
{
    /// <summary>Groups by <see cref="MonsterRef.Id"/>, preserving first-appearance order.</summary>
    public static IReadOnlyList<MonsterCount> Group(IReadOnlyList<MonsterRef> monsters)
    {
        ArgumentNullException.ThrowIfNull(monsters);

        var order = new List<string>();
        var byId = new Dictionary<string, (MonsterRef Monster, int Count)>();
        foreach (var m in monsters)
        {
            if (byId.TryGetValue(m.Id, out var existing))
            {
                byId[m.Id] = (existing.Monster, existing.Count + 1);
            }
            else
            {
                byId[m.Id] = (m, 1);
                order.Add(m.Id);
            }
        }

        return order.Select(id => new MonsterCount(byId[id].Monster, byId[id].Count)).ToList();
    }

    /// <summary>Renders the grouped counts as "1× Hobgoblin, 8× Goblin".</summary>
    public static string Describe(IReadOnlyList<MonsterRef> monsters) =>
        string.Join(", ", Group(monsters).Select(g => $"{g.Count}× {g.Monster.Name}"));
}

/// <summary>
/// The grouped, model-facing shape of a built encounter — the flat repeated monster list collapsed
/// to per-monster counts so the chat tool echoes "8× Goblin" instead of eight separate entries.
/// </summary>
public sealed record BuiltEncounterView(
    Difficulty Difficulty,
    int TotalMonsterXp,
    int AdjustedXp,
    bool FullyMatched,
    string? Note,
    IReadOnlyList<int> PartyLevels,
    IReadOnlyList<MonsterCount> Groups);
