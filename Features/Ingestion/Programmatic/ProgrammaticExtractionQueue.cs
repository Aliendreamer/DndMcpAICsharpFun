using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

public interface IProgrammaticExtractionQueue
{
    /// <summary>Enqueues a programmatic extraction; false when that book is already queued or running.</summary>
    bool TryEnqueue(int bookId);
}

/// <summary>Own channel and in-flight guard, deliberately separate from <see cref="IngestionQueueWorker"/> (runs take hours).</summary>
public sealed partial class ProgrammaticExtractionQueue(
    IServiceScopeFactory scopeFactory,
    ILogger<ProgrammaticExtractionQueue> logger) : BackgroundService, IProgrammaticExtractionQueue
{
    private readonly Channel<int> _channel =
        Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<int, byte> _inFlight = new();

    public bool TryEnqueue(int bookId)
    {
        if (!_inFlight.TryAdd(bookId, 0)) return false;
        if (_channel.Writer.TryWrite(bookId)) return true;
        _inFlight.TryRemove(bookId, out _);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var bookId in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                LogStarted(logger, bookId);
                var sw = Stopwatch.StartNew();
                var runner = scope.ServiceProvider.GetRequiredService<IProgrammaticExtractionRunner>();
                await runner.RunAsync(bookId, stoppingToken);
                LogCompleted(logger, bookId, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogUnhandledError(logger, ex, bookId);
            }
            finally
            {
                _inFlight.TryRemove(bookId, out _);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting programmatic extraction for book {BookId}")]
    private static partial void LogStarted(ILogger logger, int bookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Completed programmatic extraction for book {BookId} in {ElapsedMs}ms")]
    private static partial void LogCompleted(ILogger logger, int bookId, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Programmatic extraction failed for book {BookId}; existing canonical files left untouched")]
    private static partial void LogUnhandledError(ILogger logger, Exception ex, int bookId);
}
