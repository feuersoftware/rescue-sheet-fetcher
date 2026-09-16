using System.Collections.Concurrent;

namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// Enforces a minimum delay between requests to the same host. KBA's robots.txt sets
/// <c>Crawl-delay: 30</c> for its statistics paths; other hosts get a smaller default politeness delay.
/// Thread-safe: concurrent callers for the same host serialize on that host's lock.
///
/// The gap is measured from one request's *completion* to the next request's start, not from
/// start-to-start: <see cref="AcquireAsync"/> only computes/waits out the remaining delay and returns a
/// slot; the caller must dispose that slot immediately after the real request finishes (success,
/// failure, or a cancellation that happened *after* the request was actually sent), which is what
/// stamps "now" as the new last-request time and releases the per-host gate. Holding the gate for the
/// slot's whole lifetime (not just during the wait) also serializes the real request itself, so a slow
/// request can't let a second call sneak in early. If the wait itself is cancelled before a request was
/// ever sent, no slot is returned and no time gets stamped for a request that never happened.
/// </summary>
public sealed class HostRateLimiter(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRequestAtUtc = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IDisposable> AcquireAsync(string host, TimeSpan minDelay, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_lastRequestAtUtc.TryGetValue(host, out var last))
            {
                var elapsed = _time.GetUtcNow() - last;
                var remaining = minDelay - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, _time, ct);
                }
            }
        }
        catch
        {
            gate.Release();
            throw;
        }

        return new CompletionScope(() =>
        {
            _lastRequestAtUtc[host] = _time.GetUtcNow();
            gate.Release();
        });
    }

    private sealed class CompletionScope(Action onDispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                onDispose();
            }
        }
    }
}
