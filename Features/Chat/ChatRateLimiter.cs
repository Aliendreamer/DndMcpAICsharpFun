using System.Collections.Concurrent;

namespace DndMcpAICsharpFun.Features.Chat;

public sealed class ChatRateLimiter(int messagesPerMinute, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly ConcurrentDictionary<string, WindowCounter> _counters = new();

    public bool TryAcquire(string ip)
    {
        var now = _clock();
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

    private sealed class WindowCounter(DateTimeOffset windowStart)
    {
        public DateTimeOffset WindowStart = windowStart;
        public int Count;
    }
}
