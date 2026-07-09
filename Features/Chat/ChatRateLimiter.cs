using System.Collections.Concurrent;

namespace DndMcpAICsharpFun.Features.Chat;

public sealed class ChatRateLimiter(int messagesPerMinute, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly ConcurrentDictionary<string, WindowCounter> _counters = new();
    private DateTimeOffset _lastSweep = DateTimeOffset.MinValue;

    public bool TryAcquire(string ip)
    {
        var now = _clock();
        EvictStale(now);
        var counter = _counters.GetOrAdd(ip, _ => new WindowCounter(now));

        lock (counter)
        {
            if (now - counter.WindowStart >= TimeSpan.FromMinutes(1))
            {
                counter.Count = 0;
                counter.WindowStart = now;
            }

            if (counter.Count >= messagesPerMinute)
                return false;

            counter.Count++;
            return true;
        }
    }

    // Bound memory growth: at most once per minute, drop counters whose window has fully expired so
    // per-client state does not accumulate indefinitely as distinct IPs come and go.
    private void EvictStale(DateTimeOffset now)
    {
        if (now - _lastSweep < TimeSpan.FromMinutes(1))
            return;
        _lastSweep = now;
        foreach (var kv in _counters)
        {
            if (now - kv.Value.WindowStart >= TimeSpan.FromMinutes(2))
                _counters.TryRemove(kv.Key, out _);
        }
    }

    private sealed class WindowCounter(DateTimeOffset windowStart)
    {
        public DateTimeOffset WindowStart = windowStart;
        public int Count;
    }
}