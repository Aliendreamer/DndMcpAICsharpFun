using DndMcpAICsharpFun.Features.Ingestion.Tracking;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed partial class IngestionBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<IngestionBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan CycleInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycleAsync(stoppingToken);
            await Task.Delay(CycleInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        LogCycleStarting(logger);
        int processed = 0, failed = 0;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var tracker = scope.ServiceProvider.GetRequiredService<IIngestionTracker>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IIngestionOrchestrator>();

            var eligible = await tracker.GetPendingAndFailedAsync(ct);

            foreach (var record in eligible)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    await orchestrator.IngestBookAsync(record.Id, ct);
                    processed++;
                }
                catch (Exception ex)
                {
                    LogRecordError(logger, ex, record.Id);
                    failed++;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCycleFailed(logger, ex);
        }

        LogCycleComplete(logger, processed, failed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ingestion cycle starting")]
    private static partial void LogCycleStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error ingesting record {Id}")]
    private static partial void LogRecordError(ILogger logger, Exception ex, int id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ingestion cycle failed")]
    private static partial void LogCycleFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ingestion cycle complete. Processed={Processed} Failed={Failed}")]
    private static partial void LogCycleComplete(ILogger logger, int processed, int failed);
}
