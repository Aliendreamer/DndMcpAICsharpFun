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
        Log.CycleStarting(logger);
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
                    Log.RecordError(logger, ex, record.Id);
                    failed++;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.CycleFailed(logger, ex);
        }

        Log.CycleComplete(logger, processed, failed);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Ingestion cycle starting")]
        public static partial void CycleStarting(ILogger logger);

        [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error ingesting record {Id}")]
        public static partial void RecordError(ILogger logger, Exception ex, int id);

        [LoggerMessage(Level = LogLevel.Error, Message = "Ingestion cycle failed")]
        public static partial void CycleFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Information, Message = "Ingestion cycle complete. Processed={Processed} Failed={Failed}")]
        public static partial void CycleComplete(ILogger logger, int processed, int failed);
    }
}
