using System.Net;

using FluentAssertions;

using DndMcpAICsharpFun.Tools.ModelEval;
using DndMcpAICsharpFun.Tools.ModelEval.Full;

namespace DndMcpAICsharpFun.Tests.ModelEval;

public class FullChainRunnerTests
{
    private sealed class FakeClient(Func<int, TurnDto> onCall) : IEvalTurnClient
    {
        public int Calls;
        public Task<TurnDto> RunAsync(string prompt, CancellationToken ct) => Task.FromResult(onCall(++Calls));
    }

    private static EvalCase Case(string id = "c1") => new(
        id, "spell", "manual", "p", new[] { "t" }, null, new[] { new Fact(new[] { "ok" }) });

    private static TurnDto Good() => new("qwen", "ok", new() { "t" }, new() { new ToolCallDto("t", "{}", "r", null) }, 10);
    private static TurnDto Bad() => new("qwen", "nope", new() { "t" }, new() { new ToolCallDto("t", "{}", "r", null) }, 10);

    [Fact]
    public async Task FirstCallUnreachable_ThrowsInfra()
    {
        var client = new FakeClient(_ => throw new HttpRequestException("refused"));
        var act = () => FullChainRunner.RunAsync(new[] { Case() }, 3, client, CancellationToken.None);
        await act.Should().ThrowAsync<EvalInfraException>().WithMessage("*refused*");
    }

    [Fact]
    public async Task FirstCall401_ThrowsInfra()
    {
        var client = new FakeClient(_ => throw new HttpRequestException("HTTP 401", null, HttpStatusCode.Unauthorized));
        var act = () => FullChainRunner.RunAsync(new[] { Case() }, 3, client, CancellationToken.None);
        await act.Should().ThrowAsync<EvalInfraException>().WithMessage("*401*");
    }

    [Fact]
    public async Task MidRunTimeout_InfraErrorRun()
    {
        var client = new FakeClient(n => n == 3 ? throw new TaskCanceledException("timeout") : Good());
        var (model, results) = await FullChainRunner.RunAsync(new[] { Case() }, 3, client, CancellationToken.None);
        model.Should().Be("qwen");
        results.Should().ContainSingle();
        var runs = results[0].Runs;
        runs.Should().HaveCount(3);
        runs[2].Outcome.Should().Be(RunOutcome.InfraError);
        runs[0].Outcome.Should().Be(RunOutcome.Pass);
        client.Calls.Should().Be(3);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var client = new FakeClient(n =>
        {
            if (n == 2) { cts.Cancel(); throw new OperationCanceledException(cts.Token); }
            return Good();
        });
        var act = () => FullChainRunner.RunAsync(new[] { Case() }, 3, client, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Majority_Passed()
    {
        var two = new FakeClient(n => n == 2 ? Bad() : Good());
        var (_, r2) = await FullChainRunner.RunAsync(new[] { Case() }, 3, two, CancellationToken.None);
        r2[0].Passed.Should().BeTrue();

        var one = new FakeClient(n => n == 1 ? Good() : Bad());
        var (_, r1) = await FullChainRunner.RunAsync(new[] { Case() }, 3, one, CancellationToken.None);
        r1[0].Passed.Should().BeFalse();
    }

    [Fact]
    public void EvalArgs_RunsDefaultDependsOnMode()
    {
        EvalArgs.Parse(new[] { "--mode", "full", "--x", "y" }).Runs.Should().Be(3);
        EvalArgs.Parse(new[] { "--model", "m", "--x", "y" }).Runs.Should().Be(5);
        EvalArgs.Parse(new[] { "--mode", "full", "--runs", "7", "--x", "y" }).Runs.Should().Be(7);
    }
}
