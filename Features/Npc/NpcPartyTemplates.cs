namespace DndMcpAICsharpFun.Features.Npc;

/// <summary>Deterministic theme→ensemble templates. The theme selects a template by keyword substring
/// (first match wins) and is otherwise handed to the LLM for flavor — it is NEVER used as a monster
/// search filter. Every archetype is a member of <see cref="NpcArchetypes.Common"/> (grounded roster).</summary>
public static class NpcPartyTemplates
{
    public sealed record Template(string Name, string[] Keywords, IReadOnlyList<(string Role, string Archetype)> Roster);

    public static readonly IReadOnlyList<Template> All =
    [
        new("criminal", ["criminal", "heist", "gang", "thie", "smuggl", "crime"],
            [("leader","Bandit Captain"), ("enforcer","Thug"), ("enforcer","Thug"), ("informant","Spy")]),
        new("military", ["military", "guard", "watch", "soldier", "mercenary", "garrison"],
            [("commander","Veteran"), ("soldier","Guard"), ("soldier","Guard"), ("scout","Scout")]),
        new("cult", ["cult", "temple", "zealot", "heretic", "sect"],
            [("high priest","Cult Fanatic"), ("cultist","Cultist"), ("cultist","Cultist"), ("acolyte","Acolyte")]),
        new("noble", ["noble", "court", "political", "intrigue", "house", "aristocra"],
            [("noble","Noble"), ("bodyguard","Guard"), ("bodyguard","Guard"), ("agent","Spy")]),
        new("arcane", ["arcane", "mage", "wizard", "arcanist", "sorcer"],
            [("archmage","Mage"), ("apprentice","Acolyte"), ("warden","Guard"), ("warden","Guard")]),
    ];

    public static readonly IReadOnlyList<(string Role, string Archetype)> DefaultRoster =
        [("captain","Veteran"), ("guard","Guard"), ("guard","Guard"), ("townsfolk","Commoner")];

    public static (string Name, IReadOnlyList<(string Role, string Archetype)> Roster) Resolve(string theme)
    {
        var t = (theme ?? string.Empty).ToLowerInvariant();
        foreach (var tpl in All)
            if (tpl.Keywords.Any(k => t.Contains(k, StringComparison.Ordinal)))
                return (tpl.Name, tpl.Roster);
        return ("default", DefaultRoster);
    }
}
