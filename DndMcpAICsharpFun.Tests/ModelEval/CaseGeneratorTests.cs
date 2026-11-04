using System.Text.Json;

using DndMcpAICsharpFun.Features.Crafting;
using DndMcpAICsharpFun.Tools.ModelEval.Full;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.ModelEval;

public sealed class CaseGeneratorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "casegen-" + Guid.NewGuid().ToString("N"));

    public CaseGeneratorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private static object Class(string id, string name, int faces, params string[] prof) => new
    {
        id,
        type = "Class",
        name,
        authority = "canon",
        needsReview = false,
        fields = new { hd = new { number = 1, faces }, proficiency = prof },
    };

    private static object Spell(string id, string name, int level, string school, int feet,
        bool needsReview = false, string[]? filled = null) => new
        {
            id,
            type = "Spell",
            name,
            authority = "canon",
            needsReview,
            fields = new
            {
                level,
                school,
                range = new { type = "point", distance = new { type = "feet", amount = feet } },
                _fivetoolsFilledFields = filled ?? ["level", "school", "range"],
            },
        };

    private void Write(string file, params object[] entities) =>
        File.WriteAllText(Path.Combine(_dir, file), JsonSerializer.Serialize(new { entities }));

    private void BigFixture()
    {
        var list = new List<object>();
        for (var i = 0; i < 12; i++)
        {
            list.Add(Class($"phb14.class.c{i:00}", $"C{i}", 6 + (i % 4) * 2, "int", "wis"));
            list.Add(Spell($"phb14.spell.s{i:00}", $"S{i}", 1 + i % 9, "AEV"[i % 3].ToString(), 30 + i * 10));
        }
        Write("phb14.json", list.ToArray());
    }

    private static GenOptions Opts(int seed = 1, int count = 3, string[]? cats = null) =>
        new(seed, count, ["phb14"], cats);

    private static string Sig(IEnumerable<EvalCase> cs) => string.Join("\n", cs.Select(c =>
        c.Id + "|" + c.Prompt + "|" + string.Join(";", c.AnswerFacts.Select(f => string.Join("/", f.Variants)))
        + "|" + string.Join(";", c.ToolDataFacts!.Select(f => string.Join("/", f.Variants)))));

    [Fact]
    public void SameSeed_SameCases()
    {
        BigFixture();
        var a = CaseGenerator.Generate(_dir, Opts());
        var b = CaseGenerator.Generate(_dir, Opts());

        Sig(a).Should().Be(Sig(b));
        CaseGenerator.CaseSetHash(a, ["phb14"]).Should().Be(CaseGenerator.CaseSetHash(b, ["phb14"]));
    }

    [Fact]
    public void DifferentSeed_DifferentSelection()
    {
        BigFixture();
        var a = CaseGenerator.Generate(_dir, Opts(seed: 1));
        var b = CaseGenerator.Generate(_dir, Opts(seed: 2));

        a.Select(c => c.Id).Should().NotEqual(b.Select(c => c.Id));
    }

    [Fact]
    public void ClassWithoutHitDie_Excluded()
    {
        Write("phb14.json",
            Class("phb14.class.wizard", "Wizard", 6, "int", "wis"),
            new { id = "phb14.class.action-surge", type = "Class", name = "Action Surge", authority = "canon", needsReview = false, fields = new { } });

        var cases = CaseGenerator.Generate(_dir, Opts(count: 10, cats: ["class"]));

        cases.Should().NotBeEmpty().And.OnlyContain(c => !c.Id.Contains("action-surge"));
    }

    [Fact]
    public void ClassCases_UseBriefedPromptsAndFacts()
    {
        Write("phb14.json", Class("phb14.class.wizard", "Wizard", 6, "int", "wis"));
        var cases = new List<EvalCase>();
        for (var seed = 0; seed < 20; seed++)
            cases.AddRange(CaseGenerator.Generate(_dir, Opts(seed, 1, ["class"])));

        var hit = cases.First(c => c.Id.EndsWith(".hitdie"));
        hit.Prompt.Should().Be("What hit die does a Wizard have in the 2014 Player's Handbook?");
        hit.AnswerFacts.Single().Variants.Should().Equal("d6");
        hit.ToolDataFacts!.Single().Variants.Should().Equal("\"faces\":6", "d6");
        var saves = cases.First(c => c.Id.EndsWith(".saves"));
        saves.AnswerFacts.Select(f => f.Variants[0]).Should().Equal("Intelligence", "Wisdom");
        saves.ToolDataFacts!.First().Variants.Should().Equal("\"int\"", "Intelligence");
        hit.ExpectedTools.Should().Equal("get_entity", "search_entities", "search_lore", "search_dnd");
    }

    [Fact]
    public void SpellNeedingReview_Excluded()
    {
        Write("phb14.json",
            Spell("phb14.spell.ok", "Ok", 3, "V", 150),
            Spell("phb14.spell.bad", "Bad", 3, "V", 150, needsReview: true));

        var cases = CaseGenerator.Generate(_dir, Opts(count: 10, cats: ["spell"]));

        cases.Should().NotBeEmpty().And.OnlyContain(c => !c.Id.Contains(".bad."));
    }

    [Fact]
    public void SpellFieldNotFivetoolsFilled_NotAsked()
    {
        Write("phb14.json", Spell("phb14.spell.x", "X", 3, "V", 150, filled: ["school"]));
        var cases = new List<EvalCase>();
        for (var seed = 0; seed < 10; seed++)
            cases.AddRange(CaseGenerator.Generate(_dir, Opts(seed, 1, ["spell"])));

        cases.Should().NotBeEmpty().And.OnlyContain(c => c.Id.EndsWith(".school"));
    }

    [Fact]
    public void SpellFacts_FollowBrief()
    {
        Write("phb14.json", Spell("phb14.spell.fb", "Fireball", 3, "V", 150));
        var cases = new List<EvalCase>();
        for (var seed = 0; seed < 30; seed++)
            cases.AddRange(CaseGenerator.Generate(_dir, Opts(seed, 1, ["spell"])));

        var level = cases.First(c => c.Id.EndsWith(".level"));
        level.Prompt.Should().Be("What level is the spell Fireball in the 2014 Player's Handbook?");
        level.AnswerFacts.Single().Variants.Should().Equal("3rd-level", "3rd level", "level 3", "third-level", "third level");
        level.ToolDataFacts!.Single().Variants.Should().Equal("\"level\":3", "3rd-level", "3rd level", "level 3", "third-level", "third level");
        cases.First(c => c.Id.EndsWith(".school")).ToolDataFacts!.Single().Variants
            .Should().Equal("\"school\":\"V\"", "Evocation");
        cases.First(c => c.Id.EndsWith(".range")).AnswerFacts.Single().Variants
            .Should().Equal("150 feet", "150 ft", "150-foot");
    }

    [Fact]
    public void Cantrip_LevelNotAsked()
    {
        Write("phb14.json", Spell("phb14.spell.c", "C", 0, "V", 60));
        var cases = new List<EvalCase>();
        for (var seed = 0; seed < 20; seed++)
            cases.AddRange(CaseGenerator.Generate(_dir, Opts(seed, 1, ["spell"])));

        cases.Should().NotContain(c => c.Id.EndsWith(".level"));
    }

    [Fact]
    public void CraftingExpectation_EqualsCraftingMath()
    {
        var cases = CaseGenerator.Generate(_dir, Opts(count: 20, cats: ["crafting"]));

        var nonmagical = cases.Where(c => c.Id.StartsWith("gen.crafting.nonmagical.")).ToList();
        nonmagical.Should().HaveCount(4);
        foreach (var c in nonmagical)
        {
            var v = int.Parse(c.Id.Split('.')[^1]);
            var m = CraftingMath.CraftNonmagical(v);
            FactMatcher.AllMatch(c.AnswerFacts, $"{m.MaterialsGp} gp and {m.TotalWorkweeks} workweeks").Should().BeTrue();
            c.AnswerFacts[0].Variants.Should().Contain($"{m.MaterialsGp} gp");
            c.ExpectedTools.Should().Equal("calculate_crafting");
        }
        var vr = cases.Single(c => c.Id == "gen.crafting.magic.VeryRare");
        vr.Prompt.Should().Contain("a very rare magic item");
        vr.AnswerFacts.Select(f => f.Variants[0]).Should().Equal("20000 gp", "25 workweek");
    }

    [Fact]
    public void CraftingFacts_AreUnitQualified()
    {
        var cases = CaseGenerator.Generate(_dir, Opts(count: 20, cats: ["crafting"]));

        cases.Should().NotBeEmpty();
        cases.SelectMany(c => c.AnswerFacts).SelectMany(f => f.Variants)
            .Should().OnlyContain(v => v.Any(char.IsLetter));
        FactMatcher.AllMatch(cases.Single(c => c.Id == "gen.crafting.nonmagical.50").AnswerFacts,
            "step 1, level 3, 50 gp").Should().BeFalse();
    }

    [Fact]
    public void FewerCandidatesThanCount_ReturnsAllWithoutRepeats()
    {
        Write("phb14.json", Class("phb14.class.a", "A", 8, "str"), Class("phb14.class.b", "B", 10, "dex"));

        var cases = CaseGenerator.Generate(_dir, Opts(count: 50, cats: ["class"]));

        cases.Should().HaveCount(2);
        cases.Select(c => c.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BakAndProgressFilesIgnored()
    {
        Write("phb14.json", Class("phb14.class.a", "A", 8, "str"));
        File.WriteAllText(Path.Combine(_dir, "phb14.json.bak"), "not json");
        File.WriteAllText(Path.Combine(_dir, "phb14.progress.json"), "not json");

        var cases = CaseGenerator.Generate(_dir, Opts(count: 5, cats: ["class"]));

        cases.Should().HaveCount(1);
    }

    [Fact]
    public void Spell_OrdinalWordLevelVariant_Matches()
    {
        Write("phb14.json", Spell("phb14.spell.fireball", "Fireball", 3, "V", 150));
        var level = CaseGenerator.Generate(_dir, Opts(count: 5, cats: ["spell"])).First(c => c.Id.EndsWith(".level"));

        FactMatcher.AllMatch(level.AnswerFacts, "Fireball is a third-level spell").Should().BeTrue();
        FactMatcher.AllMatch(level.AnswerFacts, "It is a third level evocation").Should().BeTrue();
    }

    [Fact]
    public void Crafting_NumberWordWorkweekVariant_Matches()
    {
        var c = CaseGenerator.Generate(_dir, Opts(count: 20, cats: ["crafting"]))
            .Single(x => x.Id == "gen.crafting.nonmagical.150");

        FactMatcher.AllMatch(c.AnswerFacts, "75 gp of materials and three workweeks").Should().BeTrue();
    }

    private static object Monster(string id, string name, object? ac, object? hp, object? cr, bool needsReview = false)
    {
        var fields = new Dictionary<string, object?>();
        if (ac is not null) fields["ac"] = ac;
        if (hp is not null) fields["hp"] = hp;
        if (cr is not null) fields["cr"] = cr;
        return new { id, type = "Monster", name, authority = "canon", needsReview, fields };
    }

    private static GenOptions MonsterOpts(int seed = 1, int count = 3, string[]? cats = null) =>
        new(seed, count, ["mm14"], cats);

    private void MonsterFixture(int n = 30)
    {
        var list = new List<object>();
        for (var i = 0; i < n; i++)
            list.Add(Monster($"mm14.monster.m{i:00}", $"M{i}", new[] { new { ac = 16 } }, new { average = 27 }, "1"));
        Write("mm14.json", list.ToArray());
    }

    [Fact]
    public void Monster_ReviewedEntity_ProducesAcHpCrCases()
    {
        MonsterFixture();

        var cases = CaseGenerator.Generate(_dir, MonsterOpts(count: 30, cats: ["monster"]));

        cases.Should().OnlyContain(c => c.Category == "monster");
        cases.Should().Contain(c => c.Id.EndsWith(".ac"));
        cases.Should().Contain(c => c.Id.EndsWith(".hp"));
        cases.Should().Contain(c => c.Id.EndsWith(".cr"));
        var ac = cases.First(c => c.Id.EndsWith(".ac"));
        ac.Prompt.Should().Contain("2014 Monster Manual");
        ac.AnswerFacts.Single().Variants.Should().Contain(["armor class 16", "ac 16"]);
        cases.SelectMany(c => c.AnswerFacts).Should().OnlyContain(f => f.Variants.All(v => !v.All(char.IsDigit)));
    }

    [Fact]
    public void Monster_Truth_KeepsOnlyAgreeingFields_AndSkipsUnknownMonsters()
    {
        MonsterFixture(n: 2); // M0, M1: ac 16, hp 27, cr "1"
        // 5etools truth: M0 agrees on AC and HP but has CR 3; M1 is absent.
        using var doc = System.Text.Json.JsonDocument.Parse(
            """{"name":"M0","ac":[{"ac":16,"from":["natural armor"]}],"hp":{"average":27,"formula":"5d8+5"},"cr":"3"}""");
        var truth = new Dictionary<string, System.Text.Json.JsonElement> { ["m0"] = doc.RootElement.Clone() };

        var cases = CaseGenerator.Generate(_dir, new GenOptions(1, 30, ["mm14"], ["monster"], truth));

        cases.Should().OnlyContain(c => c.Id.StartsWith("gen.monster.mm14.monster.m00."));
        cases.Should().NotContain(c => c.Id.EndsWith(".cr"));
        // One template is sampled per monster; across seeds only the agreeing AC/HP templates ever appear.
        var ids = Enumerable.Range(0, 20)
            .SelectMany(s => CaseGenerator.Generate(_dir, new GenOptions(s, 30, ["mm14"], ["monster"], truth)))
            .Select(c => c.Id[(c.Id.LastIndexOf('.') + 1)..]).Distinct().ToList();
        ids.Should().BeEquivalentTo(["ac", "hp"]);
    }

    [Fact]
    public void Monster_NeedsReview_Excluded()
    {
        Write("mm14.json", Monster("mm14.monster.bad", "Bad", 12, new { average = 5 }, "1", needsReview: true));

        CaseGenerator.Generate(_dir, MonsterOpts(cats: ["monster"])).Should().BeEmpty();
    }

    [Theory]
    [InlineData(12)]
    public void Monster_AcShapes_IntListAndInt_Render(int ac)
    {
        Write("mm14.json", Monster("mm14.monster.a", "A", new[] { ac }, null, null),
            Monster("mm14.monster.b", "B", ac, null, null));

        var cases = CaseGenerator.Generate(_dir, MonsterOpts(count: 5, cats: ["monster"]));

        cases.Should().HaveCount(2);
        cases.Should().OnlyContain(c => c.AnswerFacts.Single().Variants.Contains("armor class 12"));
    }

    [Fact]
    public void Monster_CrObject_UsesCr()
    {
        Write("mm14.json", Monster("mm14.monster.d", "D", null, null, new { cr = "5", lair = "6" }));

        var c = CaseGenerator.Generate(_dir, MonsterOpts(cats: ["monster"])).Single();

        c.AnswerFacts.Single().Variants.Should().Contain("challenge rating 5");
    }

    [Fact]
    public void Monster_UnrenderableShapes_SkippedWithoutThrow()
    {
        Write("mm14.json", Monster("mm14.monster.x", "X", "see text", new { formula = "1d8" }, new[] { 1 }));

        var act = () => CaseGenerator.Generate(_dir, MonsterOpts(cats: ["monster"]));

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Fact]
    public void Monster_ToolDataVariants_IncludeRawJsonForms()
    {
        MonsterFixture();
        var cases = CaseGenerator.Generate(_dir, MonsterOpts(count: 30, cats: ["monster"]));

        var ac = cases.First(c => c.Id.EndsWith(".ac")).ToolDataFacts!.Single().Variants;
        ac.Should().Contain(["\"ac\":16", "armor class 16"]);
        cases.First(c => c.Id.EndsWith(".hp")).ToolDataFacts!.Single().Variants
            .Should().Contain(["\"average\":27", "27 hit points"]);
        cases.First(c => c.Id.EndsWith(".cr")).ToolDataFacts!.Single().Variants
            .Should().Contain(["\"cr\":\"1\"", "challenge rating 1"]);
    }

    [Fact]
    public void Monster_CategoryFilter_OnlyMonsters()
    {
        MonsterFixture(5);
        Write("phb14.json", Class("phb14.class.a", "A", 8, "str"));

        var cases = CaseGenerator.Generate(_dir,
            new GenOptions(1, 10, ["phb14", "mm14"], ["monster"]));

        cases.Should().NotBeEmpty().And.OnlyContain(c => c.Category == "monster");
    }

    [Fact]
    public void Monster_SameSeed_SameCases()
    {
        MonsterFixture();

        Sig(CaseGenerator.Generate(_dir, MonsterOpts(cats: ["monster"])))
            .Should().Be(Sig(CaseGenerator.Generate(_dir, MonsterOpts(cats: ["monster"]))));
    }

    [Fact]
    public void Monster_CrWithXpSuffix_UsesLeadingNumberInAnswersAndRawInToolData()
    {
        Write("mm14.json", Monster("mm14.monster.d", "D", null, null, "10 (5,900 XP)"));

        var c = CaseGenerator.Generate(_dir, MonsterOpts(cats: ["monster"])).Single();

        c.AnswerFacts.Single().Variants.Should().Contain("challenge rating 10")
            .And.NotContain(v => v.Contains("XP"));
        c.ToolDataFacts!.Single().Variants.Should().Contain("\"cr\":\"10 (5,900 XP)\"")
            .And.Contain("challenge rating 10");
    }

    [Fact]
    public void Monster_CrGarbage_SkipsCrTemplate()
    {
        Write("mm14.json", Monster("mm14.monster.a", "A", null, null, ":[{"),
            Monster("mm14.monster.b", "B", null, null, "?"));

        CaseGenerator.Generate(_dir, MonsterOpts(count: 5, cats: ["monster"])).Should().BeEmpty();
    }

    [Fact]
    public void Monster_FractionalCr_Kept()
    {
        Write("mm14.json", Monster("mm14.monster.h", "H", null, null, "1/2"));

        CaseGenerator.Generate(_dir, MonsterOpts(cats: ["monster"])).Single()
            .AnswerFacts.Single().Variants.Should().Contain("challenge rating 1/2");
    }

    [Fact]
    public void Monster_HpAnswerVariants_IncludeStatBlockForms()
    {
        MonsterFixture();
        var cases = CaseGenerator.Generate(_dir, MonsterOpts(count: 30, cats: ["monster"]));

        var hp = cases.First(c => c.Id.EndsWith(".hp"));
        hp.AnswerFacts.Single().Variants.Should().Contain(["hit points 27", "hp 27"]);
        hp.ToolDataFacts!.Single().Variants.Should().Contain(["hit points 27", "hp 27"]);
    }

    [Fact]
    public void BookScoping_MonstersOnlyFromMm14_ClassSpellOnlyFromOthers()
    {
        Write("phb14.json", Class("phb14.class.a", "A", 8, "str"),
            Monster("phb14.monster.stray", "Stray", 12, new { average = 5 }, "1"));
        Write("mm14.json", Monster("mm14.monster.m", "M", 12, new { average = 5 }, "1"),
            Class("mm14.class.odd", "Odd", 6, "dex"));

        var cases = CaseGenerator.Generate(_dir, new GenOptions(1, 50, ["phb14", "mm14"], null));

        cases.Where(c => c.Category == "monster").Should().NotBeEmpty()
            .And.OnlyContain(c => c.Id.Contains("mm14.monster."));
        cases.Where(c => c.Category == "class").Should().NotBeEmpty()
            .And.OnlyContain(c => c.Id.Contains("phb14.class."));
    }
}