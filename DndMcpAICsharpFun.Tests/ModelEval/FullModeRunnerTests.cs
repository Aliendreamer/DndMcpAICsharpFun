using DndMcpAICsharpFun.Tools.ModelEval;
using DndMcpAICsharpFun.Tools.ModelEval.Full;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.ModelEval;

public sealed class FullModeRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fmr-" + Guid.NewGuid().ToString("N"));

    public FullModeRunnerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string WriteManual(params (string Id, string Category)[] cases)
    {
        var items = cases.Select(c =>
            $"{{\"id\":\"{c.Id}\",\"category\":\"{c.Category}\",\"prompt\":\"p\",\"expectedTools\":[\"t\"],\"answerFacts\":[{{\"variants\":[\"x\"]}}]}}");
        var path = Path.Combine(_dir, "manual.json");
        File.WriteAllText(path, "[" + string.Join(",", items) + "]");
        return path;
    }

    private EvalArgs Args(string manual, string? baseline = null, int count = 100) =>
        EvalArgs.Parse([]) with
        {
            Mode = "full", AdminKey = "k", CanonicalDir = _dir, ManualPath = manual,
            ReportsDir = Path.Combine(_dir, "reports"), Baseline = baseline, Count = count,
        };

    [Fact]
    public void BuildCases_DuplicateIdAcrossGeneratedAndManual_Throws()
    {
        var a = Args(WriteManual(("gen.crafting.nonmagical.50", "crafting")));

        var act = () => FullModeRunner.BuildCases(a, _dir, ["phb14"], ["crafting"], 1);

        act.Should().Throw<InvalidDataException>().WithMessage("*gen.crafting.nonmagical.50*");
    }

    [Fact]
    public void BuildCases_CategoriesFilterManualCases()
    {
        var a = Args(WriteManual(("m.craft", "crafting"), ("m.other", "other")), count: 0);

        var cases = FullModeRunner.BuildCases(a, _dir, ["phb14"], ["crafting"], 1);

        cases.Select(c => c.Id).Should().Equal("m.craft");
    }

    [Fact]
    public async Task RunAsync_BadBaselinePath_ExitsOneBeforeRunning()
    {
        var a = Args(WriteManual(("m.craft", "crafting")), baseline: Path.Combine(_dir, "missing.json"));

        (await FullModeRunner.RunAsync(a)).Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_MissingBooksFile_ExitsOne()
    {
        var a = Args(Path.Combine(_dir, "none.json"));

        (await FullModeRunner.RunAsync(a with { Books = "nobook", Categories = "class" })).Should().Be(1);
    }

    [Fact]
    public void EvalArgs_UnknownMode_SetsErrorAndKeepsStubDefault()
    {
        var a = EvalArgs.Parse(["--mode", "bogus", "--runs", "1"]);

        a.Error.Should().Contain("bogus");
        a.Mode.Should().Be("stub");
        EvalArgs.Parse(["--mode", "full", "--runs", "1"]).Error.Should().BeNull();
    }
}
