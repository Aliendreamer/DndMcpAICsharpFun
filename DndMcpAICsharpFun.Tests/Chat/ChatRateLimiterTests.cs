using DndMcpAICsharpFun.Features.Chat;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Chat;

public class ChatRateLimiterTests
{
    [Fact]
    public void TryAcquire_under_limit_returns_true()
    {
        var limiter = new ChatRateLimiter(5);

        for (var i = 0; i < 5; i++)
            limiter.TryAcquire("192.168.1.1").Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_over_limit_returns_false()
    {
        var limiter = new ChatRateLimiter(3);

        limiter.TryAcquire("10.0.0.1").Should().BeTrue();
        limiter.TryAcquire("10.0.0.1").Should().BeTrue();
        limiter.TryAcquire("10.0.0.1").Should().BeTrue();
        limiter.TryAcquire("10.0.0.1").Should().BeFalse();
    }

    [Fact]
    public void TryAcquire_different_ips_have_independent_counters()
    {
        var limiter = new ChatRateLimiter(1);

        limiter.TryAcquire("1.1.1.1").Should().BeTrue();
        limiter.TryAcquire("2.2.2.2").Should().BeTrue();
        limiter.TryAcquire("1.1.1.1").Should().BeFalse();
        limiter.TryAcquire("2.2.2.2").Should().BeFalse();
    }

    [Fact]
    public void TryAcquire_counter_resets_after_window_expires()
    {
        var fakeNow = DateTimeOffset.UtcNow;
        var limiter = new ChatRateLimiter(1, () => fakeNow);

        limiter.TryAcquire("127.0.0.1").Should().BeTrue();
        limiter.TryAcquire("127.0.0.1").Should().BeFalse();

        fakeNow = fakeNow.AddMinutes(2);

        limiter.TryAcquire("127.0.0.1").Should().BeTrue();
    }
}
