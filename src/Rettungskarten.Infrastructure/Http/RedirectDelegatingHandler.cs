namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// Follows HTTP redirects inside the handler pipeline instead of leaving them to the primary handler
/// (every named client's primary handler has <c>AllowAutoRedirect</c> off). The primary handler follows
/// redirects below every delegating handler, so a redirect target - BMW's signed S3 link, Mitsubishi's
/// short download links, any CDN or host hop - used to be fetched without a robots.txt check and without
/// taking that host's rate-limit slot. Sitting outside <see cref="RobotsTxtDelegatingHandler"/>, this
/// handler sends every hop back through the robots.txt check, the rate limiter and the send timeout as a
/// request of its own. See <see cref="RedirectFollower"/> for the redirect rules.
/// </summary>
public sealed class RedirectDelegatingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        RedirectFollower.SendAsync(request, (hop, ct) => base.SendAsync(hop, ct), cancellationToken);
}

/// <summary>
/// HttpClient's own redirect rules, for code that follows redirects itself: at most
/// <see cref="MaxRedirects"/> hops; 301/302 turn a POST into a GET, 303 turns anything but HEAD into a
/// GET, 307/308 keep the method and body; never from https to http. The Authorization header is never
/// forwarded, and a Cookie header set on the request only to the same host (cookies the server set go
/// through the primary handler's own cookie container as before). The final response's
/// <see cref="HttpResponseMessage.RequestMessage"/> is the last hop, as with automatic redirects.
/// </summary>
public static class RedirectFollower
{
    public const int MaxRedirects = 10;

    public static async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken ct)
    {
        var current = request;
        for (var redirects = 0; ; redirects++)
        {
            var response = await send(current, ct);
            if (redirects == MaxRedirects || CreateRedirectRequest(current, response) is not { } next)
            {
                return response;
            }

            response.Dispose();
            current = next;
        }
    }

    /// <summary>The request for the next hop, or null if <paramref name="response"/> is not a redirect
    /// that may be followed.</summary>
    internal static HttpRequestMessage? CreateRedirectRequest(HttpRequestMessage request, HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (status is not (301 or 302 or 303 or 307 or 308) ||
            response.Headers.Location is not { } location ||
            request.RequestUri is not { IsAbsoluteUri: true } from)
        {
            return null;
        }

        var target = location.IsAbsoluteUri ? location : new Uri(from, location);
        if ((target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps) ||
            (from.Scheme == Uri.UriSchemeHttps && target.Scheme == Uri.UriSchemeHttp))
        {
            return null;
        }

        var switchToGet = status == 303
            ? request.Method != HttpMethod.Head
            : status is 301 or 302 && request.Method == HttpMethod.Post;

        var next = new HttpRequestMessage(switchToGet ? HttpMethod.Get : request.Method, target)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
            Content = switchToGet ? null : request.Content
        };

        var sameHost = string.Equals(from.IdnHost, target.IdnHost, StringComparison.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                (!sameHost && header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            next.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return next;
    }
}
