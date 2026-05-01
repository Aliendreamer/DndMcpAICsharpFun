using System.Collections.Concurrent;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed class ExtractionCancellationRegistry : IExtractionCancellationRegistry
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _active = new();

    public void Register(int bookId, CancellationTokenSource cts) =>
        _active[bookId] = cts;

    public bool Cancel(int bookId)
    {
        if (!_active.TryGetValue(bookId, out var cts)) return false;
        cts.Cancel();
        return true;
    }

    public void Unregister(int bookId) =>
        _active.TryRemove(bookId, out _);
}
