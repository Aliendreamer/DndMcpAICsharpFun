using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class IngestionBackgroundServiceTests
{
    private static (IngestionBackgroundService Service, IIngestionTracker Tracker, IIngestionOrchestrator Orchestrator)
        BuildSut(IList<IngestionRecord> pendingRecords)
    {
        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetPendingAndFailedAsync(Arg.Any<CancellationToken>())
            .Returns(pendingRecords);

        var orchestrator = Substitute.For<IIngestionOrchestrator>();

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IIngestionTracker)).Returns(tracker);
        serviceProvider.GetService(typeof(IIngestionOrchestrator)).Returns(orchestrator);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sut = new IngestionBackgroundService(scopeFactory, NullLogger<IngestionBackgroundService>.Instance);
        return (sut, tracker, orchestrator);
    }

    [Fact]
    public async Task RunCycle_NoPendingRecords_DoesNotCallOrchestrator()
    {
        var (sut, _, orchestrator) = BuildSut([]);
        var cycleComplete = new TaskCompletionSource();
        orchestrator.When(x => x.IngestBookAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()))
            .Do(_ => cycleComplete.TrySetResult());

        var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);

        // Let the cycle run; cancel after a short wait since there's nothing to signal us
        await Task.Delay(200);
        cts.Cancel();

        await orchestrator.DidNotReceive().IngestBookAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCycle_WithPendingRecords_IngestsEach()
    {
        var records = new List<IngestionRecord>
        {
            new() { Id = 10 },
            new() { Id = 11 },
        };
        var (sut, _, orchestrator) = BuildSut(records);

        var bothProcessed = new TaskCompletionSource();
        int callCount = 0;
        orchestrator.IngestBookAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (Interlocked.Increment(ref callCount) >= 2)
                    bothProcessed.TrySetResult();
                return Task.CompletedTask;
            });

        var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await bothProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await orchestrator.Received(1).IngestBookAsync(10, Arg.Any<CancellationToken>());
        await orchestrator.Received(1).IngestBookAsync(11, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCycle_OrchestratorThrows_ContinuesWithNextRecord()
    {
        var records = new List<IngestionRecord>
        {
            new() { Id = 20 },
            new() { Id = 21 },
        };
        var (sut, _, orchestrator) = BuildSut(records);

        var secondProcessed = new TaskCompletionSource();
        orchestrator.IngestBookAsync(20, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("boom")));
        orchestrator.IngestBookAsync(21, Arg.Any<CancellationToken>())
            .Returns(callInfo => { secondProcessed.TrySetResult(); return Task.CompletedTask; });

        var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await secondProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await orchestrator.Received(1).IngestBookAsync(20, Arg.Any<CancellationToken>());
        await orchestrator.Received(1).IngestBookAsync(21, Arg.Any<CancellationToken>());
    }
}
