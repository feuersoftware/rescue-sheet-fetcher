using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Shared DownloadAsync implementation for every brand source: create a client by name, hand off to
/// HttpDownloadHelper. This was duplicated byte-for-byte across 7 of the 8 *RescueCardSource classes
/// (only Porsche varied, by client name, for its ~55MB combined PDF) with no shared base - factored out
/// here behind an overridable <see cref="DownloadClientName"/> instead. Each concrete source still
/// implements <see cref="Brand"/>/<see cref="DiscoverAsync"/> itself; only the download step was ever
/// actually identical.
///
/// Three hooks cover what the non-VW sources need beyond a plain GET:
/// - <see cref="DiscoveryClientName"/>/<see cref="DownloadClientName"/> pick the named client, e.g.
///   <see cref="RettungskartenHttpClient.BrowserName"/> for Akamai-fronted hosts;
/// - <see cref="ResolveDownloadUrlAsync"/> turns an entry's stable <c>DownloadUrl</c> into the actual
///   PDF URL at download time, for sources whose real link is short-lived or one hop away (BMW's
///   signed S3 URLs expire after 2h, Mercedes' PDF link lives on a per-card detail page) - resolving
///   those during discovery would either persist dead links or cost one extra request per card on
///   every dry run;
/// - <see cref="ConfigureDownloadRequest"/> adds per-request headers (e.g. a Referer).
///
/// Also closes a latent null-safety gap: every DownloadAsync used to dereference the nullable
/// <c>entry.DownloadUrl</c> with the null-forgiving operator (<c>!</c>), relying entirely on
/// RescueCardOrchestrator already checking <c>DownloadUrl is null</c> before calling DownloadAsync -
/// the only actual protection. Any future caller that doesn't replicate that check (a retry command, a
/// direct unit test, a debug tool) would get a confusing low-level exception from deep inside
/// HttpClient instead of a clear validation error at the point of misuse.
/// <see cref="ArgumentNullException.ThrowIfNull(object?, string?)"/> here makes that explicit instead
/// of implicit.
/// </summary>
public abstract class RescueCardSourceBase(IHttpClientFactory httpClientFactory) : IRescueCardSource
{
    /// <summary>Exposed to derived classes so DiscoverAsync can reuse the same captured factory
    /// instead of each derived class capturing its own copy of the constructor parameter (which the
    /// compiler flags as CS9107 - redundant double-capture - once it's also passed to this base).</summary>
    protected IHttpClientFactory HttpClientFactory => httpClientFactory;

    public abstract Brand Brand { get; }

    /// <summary>Override for a brand whose discovery requests need a different HttpClient (e.g. the
    /// browser-header client for Akamai-fronted sites).</summary>
    protected virtual string DiscoveryClientName => RettungskartenHttpClient.Name;

    /// <summary>Override for a brand whose downloads need a different HttpClient (e.g. Porsche's
    /// long-timeout client for its ~55MB combined PDF).</summary>
    protected virtual string DownloadClientName => RettungskartenHttpClient.Name;

    protected HttpClient CreateDiscoveryClient() => httpClientFactory.CreateClient(DiscoveryClientName);

    public abstract Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct);

    /// <summary>Returns the URL to actually fetch the PDF from, or null if there is none (reported as a
    /// failed download, not an exception). The default is the entry's own <c>DownloadUrl</c>.
    /// HttpRequestException/timeouts thrown here are reported as a failed download too.</summary>
    protected virtual Task<string?> ResolveDownloadUrlAsync(RescueCardEntry entry, HttpClient client, CancellationToken ct) =>
        Task.FromResult(entry.DownloadUrl);

    protected virtual void ConfigureDownloadRequest(HttpRequestMessage request, RescueCardEntry entry)
    {
    }

    public virtual async Task<RescueCardDownloadResult> DownloadAsync(RescueCardEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry.DownloadUrl);

        var client = httpClientFactory.CreateClient(DownloadClientName);

        string? pdfUrl;
        try
        {
            pdfUrl = await ResolveDownloadUrlAsync(entry, client, ct);
        }
        catch (HttpRequestException ex)
        {
            return RescueCardDownloadResult.Fail(Strings.Get("FailureReason_DownloadUrlResolutionFailed", ex.Message), (int?)ex.StatusCode);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return RescueCardDownloadResult.Fail(Strings.Get("Http_Timeout", ex.Message));
        }

        if (string.IsNullOrWhiteSpace(pdfUrl))
        {
            return RescueCardDownloadResult.Fail(Strings.Get("FailureReason_DownloadUrlNotResolved"));
        }

        return await HttpDownloadHelper.DownloadPdfAsync(client, pdfUrl, request => ConfigureDownloadRequest(request, entry), ct);
    }
}
