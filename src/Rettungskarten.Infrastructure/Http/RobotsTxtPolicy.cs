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
/// the site simply has none) means no restrictions, and that answer is cached like real rules. A 5xx,
/// 429, timeout or network error is where this deliberately deviates from the RFC's "assume complete
/// disallow": the actual request to that host almost always fails the same way right after, and treating
/// a transient outage as "blocked" would turn one flaky response into a permanently missing card. Such a
/// failure is logged and lets the requests that were waiting for it through, but it is *not* cached - the
/// next request to that host asks again, so one transient error can't switch off a host's real rules
/// (e.g. fiat.de's <c>Disallow: *.pdf$</c>) for the rest of the run.
///
/// The fetch itself runs through the inner handlers - the per-host rate limiter and
/// <see cref="SendTimeoutDelegatingHandler"/>, which bounds it, body included (the body is already
/// buffered when <c>send</c> returns, so the read below can't stall) - and follows redirects like any request
/// (RFC 9309 asks for at least five).
/// </summary>
public sealed class RobotsTxtPolicy(ILogger<RobotsTxtPolicy> logger)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<FetchedRules>>> _rulesByOrigin =
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
        if (url.AbsolutePath.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var origin = url.GetLeftPart(UriPartial.Authority);
        var cacheKey = $"{origin}|{clientIdentity}";
        var lazy = _rulesByOrigin.GetOrAdd(cacheKey, _ => new Lazy<Task<FetchedRules>>(
            // Not tied to any one caller's token: the first request's cancellation must not fail
            // every other request waiting on the same host's rules. Each caller only stops waiting.
            () => FetchRulesAsync(new Uri($"{origin}/robots.txt"), send, copyHeaders)));

        var fetched = await lazy.Value.WaitAsync(ct);
        if (!fetched.Cacheable)
        {
            _rulesByOrigin.TryRemove(new KeyValuePair<string, Lazy<Task<FetchedRules>>>(cacheKey, lazy));
        }

        return fetched.Rules.IsAllowed(url.PathAndQuery);
    }

    private async Task<FetchedRules> FetchRulesAsync(
        Uri robotsUrl,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        Action<HttpRequestMessage> copyHeaders)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, robotsUrl);
            copyHeaders(request);
            using var response = await send(request, CancellationToken.None);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(CancellationToken.None);
                return new FetchedRules(RobotsTxtRules.Parse(content, PolitenessOptions.ProductToken), Cacheable: true);
            }

            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("{Message}", Strings.Get("Http_RobotsTxtUnavailable", robotsUrl, (int)response.StatusCode));
                return new FetchedRules(RobotsTxtRules.AllowAll, Cacheable: false);
            }

            return new FetchedRules(RobotsTxtRules.AllowAll, Cacheable: true);
        }
        catch (Exception ex)
        {
            // No caller token is involved here, so every exception is a failed fetch (network error,
            // timeout, open circuit) - never cached, see the class comment.
            logger.LogWarning("{Message}", Strings.Get("Http_RobotsTxtUnavailable", robotsUrl, ex.Message));
            return new FetchedRules(RobotsTxtRules.AllowAll, Cacheable: false);
        }
    }

    private sealed record FetchedRules(RobotsTxtRules Rules, bool Cacheable);
}
