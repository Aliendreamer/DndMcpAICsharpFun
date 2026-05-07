using System.Diagnostics;
using System.Threading.Channels;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed partial class IngestionQueueWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<IngestionQueueWorker> logger) : BackgroundService, IIngestionQueue
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
