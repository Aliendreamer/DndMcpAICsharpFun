using DndMcpAICsharpFun.Features.Ingestion;

using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class IngestionQueueWorkerTests
{
    [Fact]
    public async Task Enqueue_BlockIngest_DispatchesToBlockOrchestrator()
    {
        var orchestrator = Substitute.For<IBlockIngestionOrchestrator>();
        // Signal the moment the worker dispatches, instead of racing a fixed Task.Delay (COR-08).
        var dispatched = new TaskCompletionSource();
        orchestrator.IngestBlocksAsync(42, Arg.Any<CancellationToken>())
            .Returns(_ => { dispatched.TrySetResult(); return Task.CompletedTask; });
        var services = new ServiceCollection();
        services.AddSingleton(orchestrator);
        services.AddScoped<IBlockIngestionOrchestrator>(_ => orchestrator);
        var sp = services.BuildServiceProvider();

        var worker = new IngestionQueueWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IngestionQueueWorker>.Instance);

        using var cts = new CancellationTokenSource();
        worker.TryEnqueue(new IngestionWorkItem(IngestionWorkType.IngestBlocks, 42));
        var run = worker.StartAsync(cts.Token);

        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }
        await worker.StopAsync(CancellationToken.None);

        await orchestrator.Received(1).IngestBlocksAsync(42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryEnqueue_DuplicateBookId_WhileInFlight_IsRejected()
    {
        var orchestrator = Substitute.For<IBlockIngestionOrchestrator>();
        var dispatched = new TaskCompletionSource();
        var completionGate = new TaskCompletionSource();
        orchestrator.IngestBlocksAsync(42, Arg.Any<CancellationToken>())
            .Returns(_ => { dispatched.TrySetResult(); return completionGate.Task; });

        var services = new ServiceCollection();
        services.AddSingleton(orchestrator);
        services.AddScoped<IBlockIngestionOrchestrator>(_ => orchestrator);
        var sp = services.BuildServiceProvider();

        var worker = new IngestionQueueWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IngestionQueueWorker>.Instance);

        using var cts = new CancellationTokenSource();
        Assert.True(worker.TryEnqueue(new IngestionWorkItem(IngestionWorkType.IngestBlocks, 42)));
        var run = worker.StartAsync(cts.Token);

        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // A second enqueue for the same book id, while the first is still in-flight (the fake
        // orchestrator call is gated on completionGate), must be rejected.
        Assert.False(worker.TryEnqueue(new IngestionWorkItem(IngestionWorkType.IngestBlocks, 42)));

        completionGate.TrySetResult();
        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TryEnqueue_SameBookId_AfterJobCompletes_IsAllowedAgain()
    {
        var orchestrator = Substitute.For<IBlockIngestionOrchestrator>();
        var dispatched = new TaskCompletionSource();
        orchestrator.IngestBlocksAsync(42, Arg.Any<CancellationToken>())
            .Returns(_ => { dispatched.TrySetResult(); return Task.CompletedTask; });

        var services = new ServiceCollection();
        services.AddSingleton(orchestrator);
        services.AddScoped<IBlockIngestionOrchestrator>(_ => orchestrator);
        var sp = services.BuildServiceProvider();

        var worker = new IngestionQueueWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IngestionQueueWorker>.Instance);

        using var cts = new CancellationTokenSource();
        Assert.True(worker.TryEnqueue(new IngestionWorkItem(IngestionWorkType.IngestBlocks, 42)));
        var run = worker.StartAsync(cts.Token);

        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The job completes immediately (Task.CompletedTask); the worker's `finally` releases the
        // book id on its own continuation, so poll briefly rather than assert synchronously.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var reEnqueued = false;
        while (DateTime.UtcNow < deadline)
        {
            if (worker.TryEnqueue(new IngestionWorkItem(IngestionWorkType.IngestBlocks, 42)))
            {
                reEnqueued = true;
                break;
            }
            await Task.Delay(20);
        }

        Assert.True(reEnqueued, "book id must be released once its job completes, allowing re-enqueue");

        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }
        await worker.StopAsync(CancellationToken.None);
    }
}