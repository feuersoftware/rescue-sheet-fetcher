using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;

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

        // This runs on every outgoing request, so avoid the resource-lookup + string.Format cost of
        // Strings.Get(...) on the (default, non-verbose) common path where Debug logging is disabled.
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("{Message}", Strings.Get("Http_WaitingForRateLimitSlot", host, delay));
        }

        await rateLimiter.WaitAsync(host, delay, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
