using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// Classifies exceptions from a request sent through one of this tool's named clients. The resilience
/// pipeline reports a timed-out request as Polly's <see cref="TimeoutRejectedException"/> (not as
/// HttpClient's own <see cref="TaskCanceledException"/>) and an open circuit as
/// <see cref="BrokenCircuitException"/> - code that isolates one failed request from the rest (a skipped
/// lookup, a failed download) has to treat those like any other request failure, or a single slow
/// request escapes the isolation and fails the whole brand.
/// </summary>
public static class HttpRequestFailures
{
    /// <summary>The request timed out - as opposed to the caller cancelling it.</summary>
    public static bool IsTimeout(Exception exception, CancellationToken ct) =>
        exception is TimeoutRejectedException || (exception is TaskCanceledException && !ct.IsCancellationRequested);

    /// <summary>The request failed for a reason confined to this request (HTTP/network error, timeout,
    /// open circuit) - as opposed to the caller cancelling it or a bug.</summary>
    public static bool IsRequestFailure(Exception exception, CancellationToken ct) =>
        exception is HttpRequestException or BrokenCircuitException || IsTimeout(exception, ct);
}
