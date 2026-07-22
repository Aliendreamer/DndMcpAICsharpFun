using System.Diagnostics;

using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

// Task 4.4 (audit P3): every existing caller (orchestrator, candidate extractor, runner, decline-recovery
// tests) hardcodes MaxAttempts = 1, which only ever exercises the loop's single-iteration path — the
// retry branch (attempt < MaxAttempts), the exponential-backoff delay, and mid-delay cancellation were
// never exercised anywhere. These tests target ExtractionRetryPolicy directly, independent of the
// orchestrator, using a small real BaseDelay (no injectable clock/delay seam exists on the class) and
// elapsed-time tolerances instead of a fake clock.
public sealed class ExtractionRetryPolicyTests
{
    [Fact]
    public async Task ExecuteAsync_SecondAttemptSucceeds_ReturnsSuccessAndStopsRetrying()
    {
        var policy = new ExtractionRetryPolicy { MaxAttempts = 3, BaseDelay = TimeSpan.FromMilliseconds(5) };
        var attempts = new List<int>();

        var result = await policy.ExecuteAsync(
            (attempt, ct) =>
            {
                attempts.Add(attempt);
                return Task.FromResult(attempt == 2);
            },
            isSuccess: r => r,
            CancellationToken.None);

        result.Should().BeTrue();
        // A successful 2nd attempt must stop the loop before a 3rd is ever tried.
        attempts.Should().Equal(1, 2);
    }

    [Fact]
    public async Task ExecuteAsync_ObservesExponentialBackoffBetweenAttempts_AndDoesNotDelayAfterTheFinalAttempt()
    {
        var policy = new ExtractionRetryPolicy { MaxAttempts = 3, BaseDelay = TimeSpan.FromMilliseconds(40) };
        var attempts = new List<int>();

        var sw = Stopwatch.StartNew();
        var result = await policy.ExecuteAsync(
            (attempt, ct) =>
            {
                attempts.Add(attempt);
                return Task.FromResult(false);
            },
            isSuccess: r => r,
            CancellationToken.None);
        sw.Stop();

        attempts.Should().Equal(1, 2, 3);
        result.Should().BeFalse();

        // Two backoff delays are expected between the 3 attempts: 40ms * 2^0 + 40ms * 2^1 = 120ms.
        // Lower bound tolerates scheduler jitter; the upper bound guards against a regression that adds
        // a trailing delay after the final (3rd) attempt, which would push this well past 100ms.
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(100);
        sw.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledDuringTheBackoffDelay_ThrowsOperationCanceledException_WithNoFurtherAttempts()
    {
        var policy = new ExtractionRetryPolicy { MaxAttempts = 3, BaseDelay = TimeSpan.FromMilliseconds(500) };
        var attempts = new List<int>();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(30)); // fires mid-delay (delay is 500ms)

        var act = async () => await policy.ExecuteAsync(
            (attempt, ct) =>
            {
                attempts.Add(attempt);
                return Task.FromResult(false); // never succeeds, so the loop proceeds into the delay
            },
            isSuccess: r => r,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // Cancellation during the post-attempt-1 delay must prevent attempt 2.
        attempts.Should().Equal(1);
    }
}