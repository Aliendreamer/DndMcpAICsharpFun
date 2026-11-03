using FluentAssertions;

using DndMcpAICsharpFun.Tools.ModelEval.Full;

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
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Tools", "ModelEval", "cases", "manual.json");
        var cases = ManualCases.Load(path);
        cases.Should().NotBeEmpty();
        cases.Should().OnlyContain(c => c.Tag == "manual");
    }
}
