using System.Collections.Concurrent;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

/// <summary>
/// One writer per book's canonical data (canonical file, overrides, dnd_entities points). Shared by the programmatic
/// extraction run (around the file swap and ingest) and the review page's saves, so neither can lose the other's write.
/// Register as a singleton; bounded by the number of registered books.
/// </summary>
public sealed class CanonicalBookLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Waits for the book's lock; dispose the result to release it.</summary>
    public async Task<IDisposable> AcquireAsync(string bookSlug, CancellationToken ct)
    {
        var sem = _locks.GetOrAdd(bookSlug, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        return new Releaser(sem);
    }

    private sealed class Releaser(SemaphoreSlim sem) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) sem.Release();
        }
    }
}
