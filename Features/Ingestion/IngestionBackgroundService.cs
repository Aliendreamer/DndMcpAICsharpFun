using DndMcpAICsharpFun.Features.Ingestion.Tracking;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed class IngestionBackgroundService : BackgroundService
{
    private static readonly TimeSpan CycleInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IngestionBackgroundService> _logger;

    public IngestionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<IngestionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First run immediately on startup, then every 24 hours
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycleAsync(stoppingToken);
            await Task.Delay(CycleInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        _logger.LogInformation("Ingestion cycle starting");
        int processed = 0, failed = 0;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
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
                    _logger.LogError(ex, "Unhandled error ingesting record {Id}", record.Id);
                    failed++;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Ingestion cycle failed");
        }

        _logger.LogInformation(
            "Ingestion cycle complete. Processed={Processed} Failed={Failed}",
            processed, failed);
    }
}
