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

        _orchestrator.IngestBookAsync(bookId, Arg.Any<CancellationToken>())
                     .Returns(Task.CompletedTask);

        using var stoppingCts = new CancellationTokenSource();
        await sut.StartAsync(stoppingCts.Token);

        sut.TryEnqueue(new IngestionWorkItem(IngestionWorkType.Reingest, bookId));

        await Task.Delay(300);
        await stoppingCts.CancelAsync();

        _registry.DidNotReceive().Register(Arg.Any<int>(), Arg.Any<CancellationTokenSource>());
        _registry.DidNotReceive().Unregister(Arg.Any<int>());
    }
}
