using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;

namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// Process-wide cache of every host's robots.txt rules (fetched at most once per host and client per run), so
/// the per-request check in <see cref="RobotsTxtDelegatingHandler"/> costs one dictionary lookup
/// after the first request to a host.
///
/// What an unreadable robots.txt means follows RFC 9309 for the common case - a 4xx (including 404:
/// the site simply has none) means no restrictions. A 5xx or network error is where this deliberately
/// deviates from the RFC's "assume complete disallow": the actual request to that host almost always
/// fails the same way right after, and treating a transient outage as "blocked" would turn one flaky
/// response into a permanently missing card for the whole run. It is logged as a warning instead.
/// </summary>
public sealed class RobotsTxtPolicy(PolitenessOptions options, ILogger<RobotsTxtPolicy> logger)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<RobotsTxtRules>>> _rulesByOrigin =
        new(StringComparer.OrdinalIgnoreCase);

    /// <param name="clientIdentity">The requesting client's User-Agent. Part of the cache key because
    /// the same host can answer differently per client: an Akamai-fronted host returns 403 for
    /// robots.txt to the plain client (= "no restrictions" by the 4xx rule) but the real rules to the
    /// browser-header client - sharing one cache entry per host let whichever client asked first decide
    /// for both (found live: fiat.de's <c>Disallow: *.pdf$</c> was ignored after the plain client had
    /// cached its 403).</param>
    public async Task<bool> IsAllowedAsync(
        Uri url,
        string clientIdentity,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        Action<HttpRequestMessage> copyHeaders,
        CancellationToken ct)
    {
        if (!options.RespectRobotsTxt || url.AbsolutePath.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var origin = url.GetLeftPart(UriPartial.Authority);
        var cacheKey = $"{origin}|{clientIdentity}";
        var lazy = _rulesByOrigin.GetOrAdd(cacheKey, _ => new Lazy<Task<RobotsTxtRules>>(
            // Not tied to any one caller's token: the first request's cancellation must not fail
            // every other request waiting on the same host's rules. Each caller only stops waiting.
            () => FetchRulesAsync(new Uri($"{origin}/robots.txt"), send, copyHeaders, CancellationToken.None)));

        var rules = await lazy.Value.WaitAsync(ct);

        return rules.IsAllowed(url.PathAndQuery);
    }

    private async Task<RobotsTxtRules> FetchRulesAsync(
        Uri robotsUrl,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        Action<HttpRequestMessage> copyHeaders,
        CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, robotsUrl);
            copyHeaders(request);
            using var response = await send(request, ct);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                return RobotsTxtRules.Parse(content, PolitenessOptions.ProductToken);
            }

            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("{Message}", Strings.Get("Http_RobotsTxtUnavailable", robotsUrl, (int)response.StatusCode));
            }

            return RobotsTxtRules.AllowAll;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("{Message}", Strings.Get("Http_RobotsTxtUnavailable", robotsUrl, ex.Message));
            return RobotsTxtRules.AllowAll;
        }
    }
}
