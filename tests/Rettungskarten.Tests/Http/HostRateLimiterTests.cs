using Microsoft.Extensions.Time.Testing;
using Rettungskarten.Infrastructure.Http;

namespace Rettungskarten.Tests.Http;

public class HostRateLimiterTests
{
    [Fact]
    public async Task AcquireAsync_FirstCallForHost_ReturnsImmediately()
    {
        var time = new FakeTimeProvider();
        var limiter = new HostRateLimiter(time);

        using var slot = await limiter.AcquireAsync("example.test", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.NotNull(slot);
    }

    [Fact]
    public async Task AcquireAsync_MeasuresGapFromPreviousCompletionNotPreviousStart()
    {
        // Regression test: the gap must be measured from when the previous request *completed*
        // (slot disposed), not from when its AcquireAsync call started - a slow request that's still
        // in flight when minDelay would have elapsed since its *start* must not let a second request
        // through early.
        var time = new FakeTimeProvider();
        var limiter = new HostRateLimiter(time);
        var minDelay = TimeSpan.FromSeconds(10);

        var slot1 = await limiter.AcquireAsync("example.test", minDelay, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(8)); // simulates the "request" itself taking 8s
        slot1.Dispose(); // completes at t=8s - this is what should be measured from, not t=0

        var acquireTask = limiter.AcquireAsync("example.test", minDelay, CancellationToken.None);

        // If the bug were still present (measuring from t=0), only 2 more seconds would be needed
        // (10 - 8 already elapsed since start). Advancing by exactly that must NOT be enough.
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(acquireTask.IsCompleted);

        // The correct remaining wait is the full 10s measured from completion (t=8), i.e. until t=18.
        time.Advance(TimeSpan.FromSeconds(8));
        using var slot2 = await acquireTask;
        Assert.NotNull(slot2);
    }

    [Fact]
    public async Task AcquireAsync_CancelledWhileWaiting_DoesNotStampLastRequestTime()
    {
        // Regression test: a wait that's cancelled before any request was sent must not "burn" a
        // rate-limit slot - the next real request should still be measured from the last *successful*
        // completion, not from when the cancelled wait happened to be aborted.
        var time = new FakeTimeProvider();
        var limiter = new HostRateLimiter(time);
        var minDelay = TimeSpan.FromSeconds(10);

        using (var slot1 = await limiter.AcquireAsync("example.test", minDelay, CancellationToken.None))
        {
            // completes immediately at t=0
        }

        time.Advance(TimeSpan.FromSeconds(1)); // now t=1, 9s remaining until minDelay elapses since t=0

        using var cts = new CancellationTokenSource();
        var cancelledTask = limiter.AcquireAsync("example.test", minDelay, cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledTask);

        // Still at t=1 (no further advance). If the cancelled call had wrongly stamped "now" (t=1) as
        // the last-request time, a fresh acquire would need to wait the full 10s from here (until
        // t=11). Since it correctly did NOT stamp anything, the original t=0 stamp still applies, so
        // only the remaining 9s (until t=10) should be needed.
        var acquireTask = limiter.AcquireAsync("example.test", minDelay, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(9));
        using var slot2 = await acquireTask;
        Assert.NotNull(slot2);
    }

    [Fact]
    public async Task AcquireAsync_DifferentHosts_DoNotBlockEachOther()
    {
        var time = new FakeTimeProvider();
        var limiter = new HostRateLimiter(time);

        using var slotA = await limiter.AcquireAsync("a.example.test", TimeSpan.FromSeconds(30), CancellationToken.None);
        using var slotB = await limiter.AcquireAsync("b.example.test", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.NotNull(slotA);
        Assert.NotNull(slotB);
    }
}
