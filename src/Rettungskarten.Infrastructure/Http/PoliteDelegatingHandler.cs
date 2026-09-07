using Microsoft.Extensions.Logging;

namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// Applies per-host rate limiting to every outgoing request (see <see cref="HostRateLimiter"/>).
/// </summary>
public sealed class PoliteDelegatingHandler(
    HostRateLimiter rateLimiter,
    PolitenessOptions options,
    ILogger<PoliteDelegatingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? string.Empty;
        var delay = options.GetDelayFor(host);
        logger.LogDebug("Warte auf Rate-Limit-Slot für {Host} (min. {Delay})", host, delay);
        await rateLimiter.WaitAsync(host, delay, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
