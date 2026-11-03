using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using DndMcpAICsharpFun.Features.Crafting;

namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

internal sealed record GenOptions(
    int Seed,
    int CountPerCategory,
    IReadOnlyList<string> Books,
    IReadOnlyList<string>? Categories);

/// <summary>
/// Builds eval cases whose correct answers are known: class/spell facts from canonical entity JSON and
/// crafting figures from <see cref="CraftingMath"/>. Selection is deterministic for a given seed.
/// </summary>
internal static class CaseGenerator
{
    private static readonly string[] ClassSpellTools = ["get_entity", "search_entities", "search_lore", "search_dnd"];
    private static readonly string[] CraftingTools = ["calculate_crafting"];
    private static readonly int[] NonmagicalValues = [50, 150, 500, 1500];

    private static readonly Dictionary<string, string> Abilities = new()
    {
        ["str"] = "Strength", ["dex"] = "Dexterity", ["con"] = "Constitution",
        ["int"] = "Intelligence", ["wis"] = "Wisdom", ["cha"] = "Charisma",
    };

    private static readonly Dictionary<string, string> Schools = new()
    {
        ["A"] = "Abjuration", ["C"] = "Conjuration", ["D"] = "Divination", ["E"] = "Enchantment",
        ["V"] = "Evocation", ["I"] = "Illusion", ["N"] = "Necromancy", ["T"] = "Transmutation",
    };

    private sealed record Candidate(string Id, IReadOnlyList<Func<EvalCase>> Templates);

    public static IReadOnlyList<EvalCase> Generate(string canonicalDir, GenOptions o)
    {
        var wanted = o.Categories is null ? null : new HashSet<string>(o.Categories, StringComparer.OrdinalIgnoreCase);
        bool On(string c) => wanted is null || wanted.Contains(c);

        var classes = new List<Candidate>();
        var spells = new List<Candidate>();
        if (On("class") || On("spell"))
        {
            foreach (var book in o.Books)
            {
                var path = Path.Combine(canonicalDir, book + ".json");
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("entities", out var entities)) continue;
                foreach (var e in entities.EnumerateArray())
                {
                    var type = Str(e, "type");
                    if (type == "Class" && On("class")) AddClass(e, classes);
                    else if (type == "Spell" && On("spell")) AddSpell(e, spells);
                }
            }
        }

        var cases = new List<EvalCase>();
        if (On("class")) cases.AddRange(Sample(classes, o));
        if (On("spell")) cases.AddRange(Sample(spells, o));
        if (On("crafting")) cases.AddRange(Sample(CraftingCandidates(), o));
        return cases;
    }

    public static string CaseSetHash(IEnumerable<EvalCase> cases, IReadOnlyList<string> books)
    {
        var lines = cases.Select(c => $"{c.Id}|{c.Prompt}").OrderBy(s => s, StringComparer.Ordinal)
            .Concat(books.OrderBy(b => b, StringComparer.Ordinal));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IEnumerable<EvalCase> Sample(List<Candidate> candidates, GenOptions o)
    {
        var ordered = candidates.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var rng = new Random(o.Seed);
        for (var i = ordered.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
        }
        foreach (var c in ordered.Take(o.CountPerCategory))
            yield return c.Templates[rng.Next(c.Templates.Count)]();
    }

    // ---- class ----

    private static void AddClass(JsonElement e, List<Candidate> into)
    {
        if (!e.TryGetProperty("fields", out var f) || !f.TryGetProperty("hd", out var hd)
            || !hd.TryGetProperty("faces", out var facesEl) || !facesEl.TryGetInt32(out var faces)) return;
        var id = Str(e, "id");
        var name = Str(e, "name");
        var templates = new List<Func<EvalCase>>
        {
            () => Make($"gen.class.{id}.hitdie", "class",
                $"What hit die does a {name} have in the 2014 Player's Handbook?", ClassSpellTools,
                [[$"\"faces\":{faces}", $"d{faces}"]], [[$"d{faces}"]]),
        };

        if (f.TryGetProperty("proficiency", out var prof) && prof.ValueKind == JsonValueKind.Array)
        {
            var codes = prof.EnumerateArray().Select(p => p.GetString() ?? "")
                .Where(Abilities.ContainsKey).ToList();
            if (codes.Count > 0)
            {
                templates.Add(() => Make($"gen.class.{id}.saves", "class",
                    $"What saving throw proficiencies does a {name} get in the 2014 Player's Handbook?",
                    ClassSpellTools,
                    codes.Select(c => new[] { $"\"{c}\"", Abilities[c] }).ToList(),
                    codes.Select(c => new[] { Abilities[c] }).ToList()));
            }
        }
        into.Add(new Candidate(id, templates));
    }

    // ---- spell ----

    private static void AddSpell(JsonElement e, List<Candidate> into)
    {
        if (Str(e, "authority") != "canon") return;
        if (!e.TryGetProperty("needsReview", out var nr) || nr.ValueKind != JsonValueKind.False) return;
        if (!e.TryGetProperty("fields", out var f)) return;
        var filled = f.TryGetProperty("_fivetoolsFilledFields", out var ff) && ff.ValueKind == JsonValueKind.Array
            ? ff.EnumerateArray().Select(x => x.GetString()).ToHashSet()
            : [];
        var id = Str(e, "id");
        var name = Str(e, "name");
        string Prompt(string what) => $"What {what} the spell {name} in the 2014 Player's Handbook?";
        var templates = new List<Func<EvalCase>>();

        if (filled.Contains("level") && f.TryGetProperty("level", out var lv) && lv.TryGetInt32(out var level) && level >= 1)
        {
            var ans = new List<string> { $"{level}{Ordinal(level)}-level", $"{level}{Ordinal(level)} level", $"level {level}" };
            if (level <= OrdinalWords.Length)
            {
                ans.Add($"{OrdinalWords[level - 1]}-level");
                ans.Add($"{OrdinalWords[level - 1]} level");
            }
            templates.Add(() => Make($"gen.spell.{id}.level", "spell", Prompt("level is"), ClassSpellTools,
                [[$"\"level\":{level}"]], [ans.ToArray()]));
        }
        if (filled.Contains("school") && f.TryGetProperty("school", out var sc)
            && sc.GetString() is { } code && Schools.TryGetValue(code, out var school))
        {
            templates.Add(() => Make($"gen.spell.{id}.school", "spell", Prompt("school of magic is"), ClassSpellTools,
                [[$"\"school\":\"{code}\"", school]], [[school]]));
        }
        if (filled.Contains("range") && f.TryGetProperty("range", out var r)
            && Str(r, "type") == "point" && r.TryGetProperty("distance", out var d)
            && Str(d, "type") == "feet" && d.TryGetProperty("amount", out var amt) && amt.TryGetInt32(out var feet))
        {
            var ans = new[] { $"{feet} feet", $"{feet} ft", $"{feet}-foot" };
            templates.Add(() => Make($"gen.spell.{id}.range", "spell", Prompt("range is"), ClassSpellTools,
                [[$"\"amount\":{feet}"]], [ans]));
        }
        if (templates.Count > 0) into.Add(new Candidate(id, templates));
    }

    // ---- crafting ----

    private static List<Candidate> CraftingCandidates()
    {
        var list = new List<Candidate>();
        foreach (var v in NonmagicalValues)
        {
            var c = CraftingMath.CraftNonmagical(v);
            var mats = c.MaterialsGp.ToString("G", CultureInfo.InvariantCulture);
            var weeks = c.TotalWorkweeks.ToString("G", CultureInfo.InvariantCulture);
            var id = $"gen.crafting.nonmagical.{v}";
            list.Add(new Candidate(id, [() => Make(id, "crafting",
                $"How much do the materials cost and how many workweeks does it take to craft a nonmagical item with a market value of {v} gp?",
                CraftingTools, [[$"\"materialsGp\":{mats}"]], [Gp(mats), Weeks(weeks)])]));
        }
        foreach (var r in Enum.GetValues<Rarity>())
        {
            var c = CraftingMath.CraftMagicItem(r);
            var gold = c.GoldCostGp.ToString("G", CultureInfo.InvariantCulture);
            var weeks = c.Workweeks.ToString("G", CultureInfo.InvariantCulture);
            var id = $"gen.crafting.magic.{r}";
            var words = r == Rarity.VeryRare ? "very rare" : r.ToString().ToLowerInvariant();
            list.Add(new Candidate(id, [() => Make(id, "crafting",
                $"How much gold and how many workweeks does it take to craft a {words} magic item?",
                CraftingTools, [[$"\"goldCostGp\":{gold}"]], [Gp(gold), Weeks(weeks)])]));
        }
        return list;
    }

    // ---- helpers ----

    // Unit-qualified so a bare digit elsewhere in a reply cannot satisfy the fact.
    private static string[] Gp(string g) => [$"{g} gp", $"{g} gold", $"{g} gold pieces"];

    private static string[] Weeks(string w)
    {
        var v = new List<string> { $"{w} workweek", $"{w} week", $"{w}-week", $"{w}-workweek" };
        if (int.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= NumberWords.Length)
        {
            var word = NumberWords[n - 1];
            v.AddRange([$"{word} workweek", $"{word} week", $"{word}-week", $"{word}-workweek"]);
        }
        return [.. v];
    }

    private static readonly string[] OrdinalWords =
        ["first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth"];

    private static readonly string[] NumberWords =
        ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"];

    /// <summary>Tool-data facts = raw-JSON variants plus the matching human answer variants (lore tools return prose).</summary>
    private static EvalCase Make(string id, string category, string prompt, string[] tools,
        List<string[]> toolData, List<string[]> answers)
    {
        var answerFacts = answers.Select(a => new Fact(a)).ToList();
        // toolData[i] pairs with answers[i] (clamped to the last answer when there are more tool facts than answers).
        var toolFacts = toolData.Select((t, i) => new Fact(t.Concat(answers[Math.Min(i, answers.Count - 1)]).Distinct().ToList())).ToList();
        return new EvalCase(id, category, "generated", prompt, tools, toolFacts, answerFacts);
    }

    private static string Ordinal(int n) => n switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

    private static string Str(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? "" : "";
}
