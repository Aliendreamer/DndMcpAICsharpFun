using DndMcpAICsharpFun.Features.Downtime;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Rules;
using DndMcpAICsharpFun.Features.VectorStore;

namespace DndMcpAICsharpFun.Features.Retrieval;

/// <summary>
/// Startup guard: warns (non-fatal) when a scoped retrieval key — <see cref="RuleSources.Keys"/>,
/// <see cref="DowntimeSources.Keys"/>, or any <see cref="SettingCatalog.AllScopeKeys"/> — has zero
/// blocks in <c>dnd_blocks</c>. Scoped retrieval silently returns nothing for such a key (e.g. the
/// DMG-never-ingested gap that motivated this check), so this makes the gap visible in logs at
/// startup instead of surfacing as an empty answer at query time.
///
/// <see cref="IVectorStoreService"/> is scoped, but hosted services are singletons, so a DI scope is
/// created per <see cref="StartAsync"/> call to resolve it (mirrors <see cref="Ingestion.IngestionQueueWorker"/>).
/// Never throws — a failure to run the check itself (e.g. Qdrant unreachable) is logged at Warning
/// and startup proceeds.
/// </summary>
public sealed partial class ScopeHealthCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<ScopeHealthCheck> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var vectorStore = scope.ServiceProvider.GetRequiredService<IVectorStoreService>();
            var counts = await vectorStore.GetSourceKeyCountsAsync(cancellationToken);

            var scopeKeys = RuleSources.Keys
                .Concat(DowntimeSources.Keys)
                .Concat(SettingCatalog.AllScopeKeys)
                .ToHashSet(StringComparer.Ordinal);

            WarnOnZeroCounts(scopeKeys, counts, logger);

            var unknownCount = await vectorStore.GetUnknownSourceKeyCountAsync(cancellationToken);
            WarnOnUnknownSourceKeys(unknownCount, logger);
        }
        catch (Exception ex)
        {
            LogScopeHealthCheckFailed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Core check logic, extracted for direct unit testing: logs a Warning for every
    /// <paramref name="scopeKeys"/> entry that is absent from <paramref name="counts"/> or present
    /// with a count of 0.</summary>
    internal static void WarnOnZeroCounts(
        IEnumerable<string> scopeKeys,
        IReadOnlyDictionary<string, long> counts,
        ILogger logger)
    {
        foreach (var key in scopeKeys)
        {
            if (!counts.TryGetValue(key, out var count) || count == 0)
                LogZeroCountScopeKey(logger, key);
        }
    }

    /// <summary>Catalog-drift guard: warns once with the count when one or more <c>dnd_blocks</c>
    /// points carry a <c>source_key</c> that matches no <see cref="BookCatalog"/> key — i.e. a book
    /// was ingested but nobody registered its key in <see cref="BookCatalog"/>. Silent when 0.</summary>
    internal static void WarnOnUnknownSourceKeys(long unknownCount, ILogger logger)
    {
        if (unknownCount > 0)
            LogUnknownSourceKeyDrift(logger, unknownCount);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Scope key '{Key}' has 0 blocks in dnd_blocks — retrieval scoped to it will return nothing until it is ingested")]
    private static partial void LogZeroCountScopeKey(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "{Count} dnd_blocks points have a source_key not registered in BookCatalog — a book was likely ingested without registering its key (catalog drift)")]
    private static partial void LogUnknownSourceKeyDrift(ILogger logger, long count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scope health check failed; continuing startup")]
    private static partial void LogScopeHealthCheckFailed(ILogger logger, Exception ex);
}