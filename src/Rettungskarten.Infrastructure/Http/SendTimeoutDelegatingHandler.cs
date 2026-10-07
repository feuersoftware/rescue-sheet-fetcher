using Polly.Timeout;
using Rettungskarten.Core.Localization;

namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// The per-attempt timeout of every named client. It sits inside <see cref="PoliteDelegatingHandler"/>,
/// so it only starts once the host's rate-limit slot has been acquired: time spent queued behind other
/// requests to the same host (several brands sharing a portal, KBA's 30 s crawl-delay) never counts
/// against it. The resilience pipeline's own attempt timeout wraps every handler below it, rate limiter
/// included, which turned plain queueing into spurious timeouts and retries; it is therefore set to the
/// total budget, where it never fires first (see <see cref="HttpServiceCollectionExtensions"/>).
///
/// Throws <see cref="TimeoutRejectedException"/> like Polly's own timeout, so the outer retry handles it
/// the same way and callers only need to recognize one timeout type (<see cref="HttpRequestFailures"/>).
/// <c>timeout</c> covers sending the request and receiving the response headers. With a
/// <c>bodyTimeout</c>, the body is then read into memory here, within that time: every handler above
/// and the resilience pipeline return as soon as the headers arrive, and the clients' own
/// HttpClient.Timeout is infinite, so a host that sent its headers and then stalled would otherwise hang
/// the caller's body read forever - and with it every request waiting on a shared robots.txt fetch.
/// Nothing in this app streams a response, so buffering here costs nothing.
/// </summary>
public sealed class SendTimeoutDelegatingHandler(TimeSpan timeout, TimeSpan? bodyTimeout = null) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutSource.CancelAfter(timeout);
            try
            {
                response = await base.SendAsync(request, timeoutSource.Token);
            }
            catch (OperationCanceledException ex) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutRejectedException(Strings.Get("Http_SendTimeout", request.RequestUri!, timeout), timeout, ex);
            }
        }

        if (bodyTimeout is not { } limit)
        {
            return response;
        }

        using var bodySource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bodySource.CancelAfter(limit);
        try
        {
            await response.Content.LoadIntoBufferAsync(bodySource.Token);
            return response;
        }
        catch (OperationCanceledException ex) when (bodySource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            response.Dispose();
            throw new TimeoutRejectedException(Strings.Get("Http_SendTimeout", request.RequestUri!, limit), limit, ex);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}
