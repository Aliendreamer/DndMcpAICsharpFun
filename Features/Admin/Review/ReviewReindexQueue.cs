using System.Threading.Channels;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Programmatic;

namespace DndMcpAICsharpFun.Features.Admin.Review;

public sealed record ReviewReindexJob(string BookSlug, int BookId, string EntityId);

/// <summary>
/// Review saves and approvals write the canonical file and return at once; re-indexing the entity into
/// dnd_entities (an embedding call that waits on Ollama loading its model, ~7 s) runs here in the background.
/// </summary>
public sealed class ReviewReindexQueue
{
    private readonly Channel<ReviewReindexJob> _channel = Channel.CreateUnbounded<ReviewReindexJob>();

    public void Enqueue(ReviewReindexJob job) => _channel.Writer.TryWrite(job);

    internal ChannelReader<ReviewReindexJob> Reader => _channel.Reader;
}

public sealed class ReviewReindexWorker(
    ReviewReindexQueue queue,
    IServiceScopeFactory scopes,
    CanonicalBookLocks locks,
    ILogger<ReviewReindexWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                // Same per-book lock as review writes and extraction runs, so the re-index reads a whole file.
                using (await locks.AcquireAsync(job.BookSlug, stoppingToken))
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var ingestion = scope.ServiceProvider.GetRequiredService<IEntityIngestionOrchestrator>();
                    await ingestion.ReindexEntityAsync(job.BookId, job.EntityId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background re-index of reviewed entity {EntityId} failed", job.EntityId);
            }
        }
    }
}
