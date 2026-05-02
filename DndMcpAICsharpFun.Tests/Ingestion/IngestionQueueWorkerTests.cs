using DndMcpAICsharpFun.Features.Ingestion;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class IngestionQueueWorkerTests
{
    [Fact]
    public async Task Enqueue_BlockIngest_DispatchesToBlockOrchestrator()
    {
        var orchestrator = Substitute.For<IBlockIngestionOrchestrator>();
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

        await Task.Delay(150);
        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }
        await worker.StopAsync(CancellationToken.None);

        await orchestrator.Received(1).IngestBlocksAsync(42, Arg.Any<CancellationToken>());
    }
}
