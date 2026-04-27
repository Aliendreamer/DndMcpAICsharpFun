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
                var orchestrator = scope.ServiceProvider.GetRequiredService<IIngestionOrchestrator>();

                await (item.Type switch
                {
                    IngestionWorkType.Reingest  => orchestrator.IngestBookAsync(item.BookId, stoppingToken),
                    IngestionWorkType.Extract   => orchestrator.ExtractBookAsync(item.BookId, stoppingToken),
                    IngestionWorkType.IngestJson => orchestrator.IngestJsonAsync(item.BookId, stoppingToken),
                    _ => Task.CompletedTask
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogUnhandledError(logger, ex, item.Type, item.BookId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Unhandled error processing {Type} for book {BookId}")]
    private static partial void LogUnhandledError(ILogger logger, Exception ex, IngestionWorkType type, int bookId);
}
