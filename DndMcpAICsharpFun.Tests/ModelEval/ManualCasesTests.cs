using DndMcpAICsharpFun.Tools.ModelEval.Full;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.ModelEval;

public class ManualCasesTests
{
    private static string WriteTemp(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"manual-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        ManualCases.Load(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.json")).Should().BeEmpty();
    }

    [Fact]
    public void Load_ForcesManualTag()
    {
        var path = WriteTemp("""
            [{"id":"m.1","category":"x","tag":"generated","prompt":"p","expectedTools":["get_entity"],
              "answerFacts":[{"variants":["d6"]}]}]
            """);
        try
        {
            var cases = ManualCases.Load(path);
            cases.Should().ContainSingle();
            cases[0].Tag.Should().Be("manual");
            cases[0].AnswerFacts[0].Variants.Should().Equal("d6");
            cases[0].ToolDataFacts.Should().BeNull();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MissingPrompt_ThrowsWithId()
    {
        var path = WriteTemp("""
            [{"id":"m.bad","category":"x","expectedTools":["get_entity"],"answerFacts":[{"variants":["d6"]}]}]
            """);
        try
        {
            var act = () => ManualCases.Load(path);
            act.Should().Throw<InvalidDataException>().WithMessage("*m.bad*prompt*");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ShippedManualJson_IsValid()
    {
        var path = TestPaths.RepoFile("Tools/ModelEval/cases/manual.json");
        var cases = ManualCases.Load(path);
        cases.Should().NotBeEmpty();
        cases.Should().OnlyContain(c => c.Tag == "manual");
    }

    [Fact]
    public void ShippedManualFile_LoadsAllCategories()
    {
        var cases = ManualCases.Load(TestPaths.RepoFile("Tools/ModelEval/cases/manual.json"));
        cases.Select(c => c.Id).Should().OnlyHaveUniqueItems();
        var counts = cases.GroupBy(c => c.Category).ToDictionary(g => g.Key, g => g.Count());
        counts["rules"].Should().Be(5);
        counts["lore"].Should().Be(4);
        counts["npc"].Should().Be(2);
        counts["not-found"].Should().Be(2);
    }

    [Fact]
    public void ShippedManual_NpcCases_DoNotUseBareHp()
    {
        var cases = ManualCases.Load(TestPaths.RepoFile("Tools/ModelEval/cases/manual.json"));

        foreach (var id in new[] { "manual.npc.single", "manual.npc.party" })
            cases.Single(c => c.Id == id).AnswerFacts.SelectMany(f => f.Variants).Should().NotContain("hp");
    }

    [Fact]
    public void ShippedManual_OpportunityAttack_RequiresLeavingReach()
    {
        var c = ManualCases.Load(TestPaths.RepoFile("Tools/ModelEval/cases/manual.json"))
            .Single(x => x.Id == "manual.rules.opportunity-attack");
        var expected = new[] { "leave its reach", "leaves its reach", "leaving its reach", "leave your reach",
            "leaves your reach", "out of its reach", "out of reach", "moves out of" };

        c.AnswerFacts.Single().Variants.Should().BeEquivalentTo(expected);
        c.ToolDataFacts!.Single().Variants.Should().BeEquivalentTo(expected);
        FactMatcher.AllMatch(c.AnswerFacts, "when a creature enters its reach").Should().BeFalse();
    }

    [Fact]
    public void ShippedManual_Grappled_AcceptsZeroPhrasings()
    {
        var c = ManualCases.Load(TestPaths.RepoFile("Tools/ModelEval/cases/manual.json"))
            .Single(x => x.Id == "manual.rules.grappled");
        string[] extra = ["reduced to 0", "drops to 0", "becomes zero", "speed to zero", "speed is zero"];

        c.AnswerFacts.Single().Variants.Should().Contain(extra);
        c.ToolDataFacts!.Single().Variants.Should().Contain(extra);
    }

    private static void AssertThrows(string json, string messagePattern)
    {
        var path = WriteTemp(json);
        try
        {
            var act = () => ManualCases.Load(path);
            act.Should().Throw<InvalidDataException>().WithMessage(messagePattern);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_EmptyAnswerFacts_Throws() =>
        AssertThrows("""[{"id":"m.e","prompt":"p","expectedTools":["t"],"answerFacts":[]}]""", "*m.e*answerFacts*");

    [Fact]
    public void Load_EmptyVariants_Throws() =>
        AssertThrows("""[{"id":"m.v","prompt":"p","expectedTools":["t"],"answerFacts":[{"variants":[]}]}]""", "*m.v*variants*");

    [Fact]
    public void Load_BlankVariant_Throws() =>
        AssertThrows("""[{"id":"m.b","prompt":"p","expectedTools":["t"],"answerFacts":[{"variants":[" "]}]}]""", "*m.b*blank*");

    [Fact]
    public void Load_BadToolDataFacts_Throws() =>
        AssertThrows("""[{"id":"m.t","prompt":"p","expectedTools":["t"],"toolDataFacts":[{"variants":[]}],"answerFacts":[{"variants":["a"]}]}]""", "*m.t*toolDataFacts*");

    [Fact]
    public void Load_MissingExpectedTools_Throws() =>
        AssertThrows("""[{"id":"m.x","prompt":"p","answerFacts":[{"variants":["a"]}]}]""", "*m.x*expectedTools*");

    [Fact]
    public void Load_MalformedJson_Throws()
    {
        var path = WriteTemp("[{ not json");
        try
        {
            var act = () => ManualCases.Load(path);
            act.Should().Throw<Exception>();
        }
        finally { File.Delete(path); }
    }
}