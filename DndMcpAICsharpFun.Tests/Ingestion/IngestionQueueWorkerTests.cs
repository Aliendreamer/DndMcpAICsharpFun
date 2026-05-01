using DndMcpAICsharpFun.Features.Ingestion;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class IngestionQueueWorkerTests
{
    private readonly IIngestionOrchestrator _orchestrator = Substitute.For<IIngestionOrchestrator>();
    private readonly IExtractionCancellationRegistry _registry = Substitute.For<IExtractionCancellationRegistry>();

    private IngestionQueueWorker BuildSut()
    {
        var services = new ServiceCollection();
        services.AddScoped<IIngestionOrchestrator>(_ => _orchestrator);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        return new IngestionQueueWorker(scopeFactory, NullLogger<IngestionQueueWorker>.Instance, _registry);
    }

    [Fact]
    public async Task Extract_RegistersAndUnregisters_CtsAroundExtractCall()
    {
        const int bookId = 7;
        var sut = BuildSut();

        // Track the order of calls
        var callOrder = new List<string>();
        _registry.When(r => r.Register(bookId, Arg.Any<CancellationTokenSource>()))
                 .Do(_ => callOrder.Add("Register"));
        _orchestrator.ExtractBookAsync(bookId, Arg.Any<CancellationToken>())
                     .Returns(_ => { callOrder.Add("Extract"); return Task.CompletedTask; });
        _registry.When(r => r.Unregister(bookId))
                 .Do(_ => callOrder.Add("Unregister"));

        using var stoppingCts = new CancellationTokenSource();
        await sut.StartAsync(stoppingCts.Token);

        sut.TryEnqueue(new IngestionWorkItem(IngestionWorkType.Extract, bookId));

        // Give the worker a moment to process the item
        await Task.Delay(300);
        await stoppingCts.CancelAsync();

        _registry.Received(1).Register(bookId, Arg.Any<CancellationTokenSource>());
        _registry.Received(1).Unregister(bookId);
        await _orchestrator.Received(1).ExtractBookAsync(bookId, Arg.Any<CancellationToken>());

        Assert.Equal(["Register", "Extract", "Unregister"], callOrder);
    }

    [Fact]
    public async Task Extract_UnregistersEvenWhenOrchestratorThrows()
    {
        const int bookId = 42;
        var sut = BuildSut();

        _orchestrator.ExtractBookAsync(bookId, Arg.Any<CancellationToken>())
                     .Returns(_ => Task.FromException(new InvalidOperationException("boom")));

        using var stoppingCts = new CancellationTokenSource();
        await sut.StartAsync(stoppingCts.Token);

        sut.TryEnqueue(new IngestionWorkItem(IngestionWorkType.Extract, bookId));

        await Task.Delay(300);
        await stoppingCts.CancelAsync();

        _registry.Received(1).Register(bookId, Arg.Any<CancellationTokenSource>());
        _registry.Received(1).Unregister(bookId);
    }

    [Fact]
    public async Task NonExtract_DoesNotCallRegistryAtAll()
    {
        const int bookId = 3;
        var sut = BuildSut();

        _orchestrator.IngestJsonAsync(bookId, Arg.Any<CancellationToken>())
                     .Returns(Task.CompletedTask);

        using var stoppingCts = new CancellationTokenSource();
        await sut.StartAsync(stoppingCts.Token);

        sut.TryEnqueue(new IngestionWorkItem(IngestionWorkType.IngestJson, bookId));

        await Task.Delay(300);
        await stoppingCts.CancelAsync();

        _registry.DidNotReceive().Register(Arg.Any<int>(), Arg.Any<CancellationTokenSource>());
        _registry.DidNotReceive().Unregister(Arg.Any<int>());
    }

    [Fact]
    public async Task Extract_PerBookCancellation_DoesNotKillWorkerLoop()
    {
        // Arrange: first item throws OCE (per-book cancel), second item should still be processed
        const int bookId1 = 10;
        const int bookId2 = 20;
        var sut = BuildSut();

        var secondItemProcessed = new TaskCompletionSource<bool>();

        _orchestrator.ExtractBookAsync(bookId1, Arg.Any<CancellationToken>())
                     .Returns(_ => Task.FromException(new OperationCanceledException("user cancelled this book")));

        _orchestrator.ExtractBookAsync(bookId2, Arg.Any<CancellationToken>())
                     .Returns(_ =>
                     {
                         secondItemProcessed.TrySetResult(true);
                         return Task.CompletedTask;
                     });

        using var stoppingCts = new CancellationTokenSource();
        await sut.StartAsync(stoppingCts.Token);

        // Act: enqueue both items
        sut.TryEnqueue(new IngestionWorkItem(IngestionWorkType.Extract, bookId1));
        sut.TryEnqueue(new IngestionWorkItem(IngestionWorkType.Extract, bookId2));

        // Assert: second item is processed (worker loop kept running after OCE from first item)
        var completed = await Task.WhenAny(secondItemProcessed.Task, Task.Delay(2000));
        await stoppingCts.CancelAsync();

        Assert.True(secondItemProcessed.Task.IsCompletedSuccessfully,
            "Worker loop should continue after a per-book OperationCanceledException");
        await _orchestrator.Received(1).ExtractBookAsync(bookId2, Arg.Any<CancellationToken>());
    }
}
