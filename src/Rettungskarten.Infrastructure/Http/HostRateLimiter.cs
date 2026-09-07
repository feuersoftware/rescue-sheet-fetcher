using System.Collections.Concurrent;

namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// Enforces a minimum delay between requests to the same host. KBA's robots.txt sets
/// <c>Crawl-delay: 30</c> for its statistics paths; other hosts get a smaller default politeness delay.
/// Thread-safe: concurrent callers for the same host serialize on that host's lock.
/// </summary>
public sealed class HostRateLimiter(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRequestAtUtc = new(StringComparer.OrdinalIgnoreCase);

    public async Task WaitAsync(string host, TimeSpan minDelay, CancellationToken ct)
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
        finally
        {
            _lastRequestAtUtc[host] = _time.GetUtcNow();
            gate.Release();
        }
    }
}
