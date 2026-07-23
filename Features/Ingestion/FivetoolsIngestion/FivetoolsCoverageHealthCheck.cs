using DndMcpAICsharpFun.Features.Ingestion.Tracking;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>
/// Startup guard (Task 6, surface c): warns (non-fatal) for every registered official book (has a
/// <c>FivetoolsSourceKey</c>) whose <see cref="FivetoolsCoverageService"/> coverage against 5etools is
/// below 100% — mirrors <see cref="Retrieval.ScopeHealthCheck"/>'s shape. Never blocks startup; a
/// failure to run the check itself (e.g. a book's 5etools/canonical files unreadable) is logged at
/// Warning and startup proceeds. Silent for a book already at (or vacuously above, i.e. nothing to
/// cover) 100%.
///
/// <see cref="IIngestionTracker"/> and <see cref="FivetoolsCoverageService"/> are resolved from a
/// per-<see cref="StartAsync"/> DI scope (mirrors <see cref="Retrieval.ScopeHealthCheck"/> /
/// <see cref="IngestionQueueWorker"/>) since the tracker is Scoped.
/// </summary>
public sealed partial class FivetoolsCoverageHealthCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<FivetoolsCoverageHealthCheck> logger) : IHostedService
{
    private const int PageSize = 200;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var tracker = scope.ServiceProvider.GetRequiredService<IIngestionTracker>();
            var coverageService = scope.ServiceProvider.GetRequiredService<FivetoolsCoverageService>();

            var offset = 0;
            while (true)
            {
                var page = await tracker.GetAllAsync(PageSize, offset, cancellationToken);
                if (page.Count == 0)
                    break;

                foreach (var record in page)
                {
                    if (string.IsNullOrWhiteSpace(record.FivetoolsSourceKey))
                        continue;

                    var coverage = await coverageService.ComputeAsync(record, cancellationToken);
                    WarnIfBelowFull(coverage, logger);
                }

                if (page.Count < PageSize)
                    break;
                offset += PageSize;
            }
        }
        catch (Exception ex)
        {
            LogCoverageHealthCheckFailed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Core check logic, extracted for direct unit testing: logs one Warning when
    /// <paramref name="coverage"/> is below 100%; silent at (or vacuously above, i.e.
    /// <c>TotalRoster == 0</c>) 100%.</summary>
    internal static void WarnIfBelowFull(BookCoverage coverage, ILogger logger)
    {
        if (coverage.TotalRoster == 0 || coverage.CoveragePct >= 100.0)
            return;

        var missing = coverage.TotalRoster - coverage.TotalPresent;
        LogBelowFullCoverage(logger, coverage.SourceKey, coverage.CoveragePct, missing, coverage.PerType.Count);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "{Key} coverage {Pct}% ({Missing} missing across {Types} types)")]
    private static partial void LogBelowFullCoverage(ILogger logger, string key, double pct, int missing, int types);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fivetools coverage health check failed; continuing startup")]
    private static partial void LogCoverageHealthCheckFailed(ILogger logger, Exception ex);
}