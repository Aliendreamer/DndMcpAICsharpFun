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
        id, type = "Class", name, authority = "canon", needsReview = false,
        fields = new { hd = new { number = 1, faces }, proficiency = prof },
    };

    private static object Spell(string id, string name, int level, string school, int feet,
        bool needsReview = false, string[]? filled = null) => new
    {
        id, type = "Spell", name, authority = "canon", needsReview,
        fields = new
        {
            level, school,
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
        level.AnswerFacts.Single().Variants.Should().Equal("3rd-level", "3rd level", "level 3");
        level.ToolDataFacts!.Single().Variants.Should().Equal("\"level\":3", "3rd-level", "3rd level", "level 3");
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
            c.AnswerFacts[0].Variants.Should().Contain(m.MaterialsGp.ToString());
            c.ExpectedTools.Should().Equal("calculate_crafting");
        }
        var vr = cases.Single(c => c.Id == "gen.crafting.magic.VeryRare");
        vr.Prompt.Should().Contain("a very rare magic item");
        vr.AnswerFacts.Select(f => f.Variants[0]).Should().Equal("20000", "25");
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
}
