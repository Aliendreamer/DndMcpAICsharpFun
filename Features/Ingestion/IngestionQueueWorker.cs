using System.Diagnostics;
using System.Threading.Channels;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed partial class IngestionQueueWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<IngestionQueueWorker> logger) : BackgroundService, IIngestionQueue
{
    private readonly Channel<IngestionWorkItem> _channel =
        Channel.CreateUnbounded<IngestionWorkItem>(new UnboundedChannelOptions { SingleReader = true });

    // Book ids currently enqueued or in-flight — guards against a double-click/retry race
    // enqueueing the same book twice before the first item is dequeued (TOCTOU: IngestionStatus
    // only flips to a "processing" value AFTER the worker dequeues, not at enqueue time).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> _inFlight = new();

    public bool TryEnqueue(IngestionWorkItem item)
    {
        if (!_inFlight.TryAdd(item.BookId, 0)) return false;

        if (_channel.Writer.TryWrite(item)) return true;

        _inFlight.TryRemove(item.BookId, out _);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();

                LogWorkItemStarted(logger, item.Type, item.BookId);
                var sw = Stopwatch.StartNew();

                switch (item.Type)
                {
                    case IngestionWorkType.IngestBlocks:
                        var blockOrchestrator = scope.ServiceProvider.GetRequiredService<IBlockIngestionOrchestrator>();
                        await blockOrchestrator.IngestBlocksAsync(item.BookId, stoppingToken);
                        break;
                    case IngestionWorkType.IngestEntities:
                        var entityOrchestrator = scope.ServiceProvider.GetRequiredService<Entities.IEntityIngestionOrchestrator>();
                        await entityOrchestrator.IngestEntitiesAsync(item.BookId, stoppingToken);
                        break;
                    case IngestionWorkType.ExtractEntities:
                        var extractor = scope.ServiceProvider.GetRequiredService<EntityExtraction.IEntityExtractionOrchestrator>();
                        await extractor.ExtractAsync(item.BookId, item.Force, item.ErrorsOnly, stoppingToken);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown ingestion work type: {item.Type}");
                }

                LogWorkItemCompleted(logger, item.Type, item.BookId, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw; // host is shutting down — propagate
            }
            catch (Exception ex)
            {
                LogUnhandledError(logger, ex, item.Type, item.BookId);
            }
            finally
            {
                // Job is no longer in-flight whether it succeeded, failed, or was cancelled —
                // release the book id so a follow-up request (e.g. a retry) can be enqueued.
                _inFlight.TryRemove(item.BookId, out _);
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