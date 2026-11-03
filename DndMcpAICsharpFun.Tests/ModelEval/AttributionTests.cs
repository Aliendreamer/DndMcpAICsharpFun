using FluentAssertions;

using DndMcpAICsharpFun.Tools.ModelEval.Full;

namespace DndMcpAICsharpFun.Tests.ModelEval;

public class AttributionTests
{
    private static Fact F(params string[] v) => new(v);

    private static EvalCase Case(IReadOnlyList<Fact>? data = null) => new(
        "c1", "spell", "manual", "What is Fireball?",
        new[] { "lookup_spell", "search_lore" }, data, new[] { F("3rd-level", "3rd level") });

    private static TurnDto Turn(string text, string[] offered, params ToolCallDto[] calls) =>
        new("m", text, offered.ToList(), calls.ToList(), 42);

    [Fact]
    public void NotOffered_WrongToolNotOffered()
    {
        var s = Attribution.Score(Case(), Turn("x", new[] { "other" }));
        s.Outcome.Should().Be(RunOutcome.WrongTool);
        s.Reason.Should().Be("not-offered");
    }

    [Fact]
    public void OfferedNotCalled_WrongToolNotChosen()
    {
        var s = Attribution.Score(Case(), Turn("x", new[] { "lookup_spell" }, new ToolCallDto("other", "{}", "r", null)));
        s.Outcome.Should().Be(RunOutcome.WrongTool);
        s.Reason.Should().Be("not-chosen");
    }

    [Fact]
    public void CalledWithError_BindError()
    {
        var s = Attribution.Score(Case(), Turn("x", new[] { "lookup_spell" },
            new ToolCallDto("lookup_spell", "{}", null, "missing required parameter")));
        s.Outcome.Should().Be(RunOutcome.BindError);
        s.Reason.Should().Contain("missing");
    }

    [Fact]
    public void DataMissingFact_WrongToolData()
    {
        var s = Attribution.Score(Case(new[] { F("\"level\":3") }), Turn("3rd-level", new[] { "lookup_spell" },
            new ToolCallDto("lookup_spell", "{}", "{\"level\":5}", null)));
        s.Outcome.Should().Be(RunOutcome.WrongToolData);
    }

    [Fact]
    public void DataOkAnswerWrong_WrongAnswer()
    {
        var s = Attribution.Score(Case(new[] { F("\"level\":3") }), Turn("It is 2nd-level", new[] { "lookup_spell" },
            new ToolCallDto("lookup_spell", "{}", "{\"level\":3}", null)));
        s.Outcome.Should().Be(RunOutcome.WrongAnswer);
    }

    private static EvalCase NotFoundCase() => new(
        "nf1", "not-found", "manual", "What does Zorblax's Shimmering Hexflame do?",
        new[] { "search_lore" }, null,
        new[] { F("couldn't find", "could not find", "couldnt find", "not find", "no record", "not found", "don't have", "do not have", "no information") });

    [Fact]
    public void NotFoundCase_InventedAnswer_Fails()
    {
        var s = Attribution.Score(NotFoundCase(),
            Turn("Zorblax's Shimmering Hexflame is a 3rd-level evocation spell", new[] { "search_lore" },
                new ToolCallDto("search_lore", "{}", "[]", null)));
        s.Outcome.Should().Be(RunOutcome.WrongAnswer);
    }

    [Fact]
    public void NotFoundCase_CouldNotFindReply_Passes()
    {
        var s = Attribution.Score(NotFoundCase(),
            Turn("I couldn't find that spell in your books", new[] { "search_lore" },
                new ToolCallDto("search_lore", "{}", "[]", null)));
        s.Outcome.Should().Be(RunOutcome.Pass);
    }

    [Fact]
    public void AnyAcceptableToolPasses()
    {
        var s = Attribution.Score(Case(), Turn("A 3rd-level spell", new[] { "lookup_spell", "search_lore" },
            new ToolCallDto("search_lore", "{}", "lore", null)));
        s.Outcome.Should().Be(RunOutcome.Pass);
        s.LatencyMs.Should().Be(42);
    }

    [Fact]
    public void NullToolDataFacts_SkipsDataCheck()
    {
        var s = Attribution.Score(Case(null), Turn("3rd level", new[] { "lookup_spell" },
            new ToolCallDto("lookup_spell", "{}", "anything", null)));
        s.Outcome.Should().Be(RunOutcome.Pass);
    }

    [Fact]
    public void DataMissing_NotFoundText_ReasonNotFound()
    {
        var s = Attribution.Score(Case(new[] { F("\"level\":3") }), Turn("x", new[] { "lookup_spell" },
            new ToolCallDto("lookup_spell", "{}", "Entity phb14.spell.zzz Not Found", null)));
        s.Outcome.Should().Be(RunOutcome.WrongToolData);
        s.Reason.Should().Be("not-found");
        EvalReport.Label(s).Should().Be("wrong-tool-data:not-found");
    }

    [Fact]
    public void DataMissing_NoEntitiesFound_ReasonNotFound()
    {
        var s = Attribution.Score(Case(new[] { F("\"level\":3") }), Turn("x", new[] { "lookup_spell" },
            new ToolCallDto("lookup_spell", "{}", "no entities found for query", null)));
        s.Reason.Should().Be("not-found");
    }

    [Fact]
    public void DataMissing_OtherText_NoReason_PlainLabel()
    {
        var s = Attribution.Score(Case(new[] { F("\"level\":3") }), Turn("x", new[] { "lookup_spell" },
            new ToolCallDto("lookup_spell", "{}", "{\"level\":2}", null)));
        s.Reason.Should().BeNull();
        EvalReport.Label(s).Should().Be("wrong-tool-data");
    }
}
