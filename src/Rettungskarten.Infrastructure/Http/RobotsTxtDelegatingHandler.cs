using Rettungskarten.Core.Localization;

namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// Refuses any request whose URL the target host's robots.txt disallows for this tool (see
/// <see cref="RobotsTxtPolicy"/>/<see cref="RobotsTxtRules"/>). Sits in every named client's pipeline
/// rather than in one brand source, so no source - existing or future - can forget the check.
///
/// A refused request never reaches the network: it gets a synthetic <c>451</c> response marked with
/// <see cref="BlockedMarkerHeader"/>. A response rather than an exception on purpose - the outer
/// resilience handler would retry an exception (with backoff) for a result that can never change,
/// while a 451 is not a transient status and passes straight through.
/// <see cref="HttpDownloadHelper"/> turns the marker into a localized "blocked by robots.txt" failure
/// reason; a blocked discovery page simply fails discovery like any other non-success status.
///
/// Registered inside <see cref="RedirectDelegatingHandler"/> (every redirect hop is checked as a
/// request of its own) and outside the rate limiter, so the robots.txt fetch itself is rate-limited
/// and time-bounded like every other request to that host. Skipped only for requests inside an
/// explicit <see cref="RobotsTxtBypass"/> scope.
/// </summary>
public sealed class RobotsTxtDelegatingHandler(RobotsTxtPolicy policy) : DelegatingHandler
{
    public const string BlockedMarkerHeader = "X-Rettungskarten-Blocked-By";
    private const int UnavailableForLegalReasons = 451;

    public static bool IsBlockedResponse(HttpResponseMessage response) =>
        response.Headers.Contains(BlockedMarkerHeader);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not { IsAbsoluteUri: true } uri || RobotsTxtBypass.IsActive)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var allowed = await policy.IsAllowedAsync(
            uri,
            request.Headers.UserAgent.ToString(),
            (robotsRequest, ct) => RedirectFollower.SendAsync(robotsRequest, (hop, hopCt) => base.SendAsync(hop, hopCt), ct),
            robotsRequest => CopyHeaders(request, robotsRequest),
            cancellationToken);

        if (allowed)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var blocked = new HttpResponseMessage((System.Net.HttpStatusCode)UnavailableForLegalReasons)
        {
            ReasonPhrase = Strings.Get("FailureReason_RobotsTxtDisallowed"),
            RequestMessage = request
        };
        blocked.Headers.Add(BlockedMarkerHeader, "robots.txt");
        return blocked;
    }

    /// <summary>The client's default headers (User-Agent, Accept, Sec-Fetch-*) are already merged into
    /// the original request by the time it reaches a handler - copying them keeps the robots.txt
    /// request indistinguishable from the real one (a bot-protected host answering the real request
    /// but blocking a bare robots.txt fetch would otherwise make its rules invisible).</summary>
    private static void CopyHeaders(HttpRequestMessage source, HttpRequestMessage target)
    {
        foreach (var header in source.Headers)
        {
            if (header.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            target.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}
