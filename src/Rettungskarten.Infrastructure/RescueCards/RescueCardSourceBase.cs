using Rettungskarten.Core.Abstractions;
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

    /// <summary>Override for a brand whose downloads need a different HttpClient (e.g. Porsche's
    /// long-timeout client for its ~55MB combined PDF).</summary>
    protected virtual string DownloadClientName => RettungskartenHttpClient.Name;

    public abstract Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct);

    public virtual async Task<RescueCardDownloadResult> DownloadAsync(RescueCardEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry.DownloadUrl);

        var client = httpClientFactory.CreateClient(DownloadClientName);
        return await HttpDownloadHelper.DownloadPdfAsync(client, entry.DownloadUrl, ct);
    }
}
