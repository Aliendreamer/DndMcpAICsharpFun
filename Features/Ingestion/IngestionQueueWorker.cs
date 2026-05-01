using System.Diagnostics;
using System.Threading.Channels;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed partial class IngestionQueueWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<IngestionQueueWorker> logger,
    IExtractionCancellationRegistry cancellationRegistry) : BackgroundService, IIngestionQueue
{
    private readonly Channel<IngestionWorkItem> _channel =
        Channel.CreateUnbounded<IngestionWorkItem>(new UnboundedChannelOptions { SingleReader = true });

    public bool TryEnqueue(IngestionWorkItem item) => _channel.Writer.TryWrite(item);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IIngestionOrchestrator>();

                LogWorkItemStarted(logger, item.Type, item.BookId);
                var sw = Stopwatch.StartNew();

                if (item.Type == IngestionWorkType.Extract)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    cancellationRegistry.Register(item.BookId, cts);
                    try
                    {
                        await orchestrator.ExtractBookAsync(item.BookId, cts.Token);
                    }
                    finally
                    {
                        cancellationRegistry.Unregister(item.BookId);
                    }
                }
                else
                {
                    if (item.Type == IngestionWorkType.IngestJson)
                        await orchestrator.IngestJsonAsync(item.BookId, stoppingToken);
                }

                LogWorkItemCompleted(logger, item.Type, item.BookId, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw; // host is shutting down — propagate
            }
            catch (OperationCanceledException)
            {
                // user-triggered cancel for this book — orchestrator already cleaned up; continue loop
            }
            catch (Exception ex)
            {
                LogUnhandledError(logger, ex, item.Type, item.BookId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting {Type} for book {BookId}")]
    private static partial void LogWorkItemStarted(ILogger logger, IngestionWorkType type, int bookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Completed {Type} for book {BookId} in {ElapsedMs}ms")]
    private static partial void LogWorkItemCompleted(ILogger logger, IngestionWorkType type, int bookId, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Unhandled error processing {Type} for book {BookId}")]
    private static partial void LogUnhandledError(ILogger logger, Exception ex, IngestionWorkType type, int bookId);
}
