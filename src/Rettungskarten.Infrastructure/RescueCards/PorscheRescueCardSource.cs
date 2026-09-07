using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// No confirmed public source for Porsche rescue data sheets was found during research. This stub
/// exists so Porsche can be registered like every other brand and show up as "not implemented" in run
/// summaries, rather than being silently absent from the brand list.
/// </summary>
public sealed class PorscheRescueCardSource : IRescueCardSource
{
    public Brand Brand => Brand.Porsche;

    public Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct) =>
        throw new NotSupportedException("Porsche: keine bestätigte öffentliche Quelle für Rettungsdatenblätter gefunden (Stand 2026-09).");

    public Task<RescueCardDownloadResult> DownloadAsync(RescueCardEntry entry, CancellationToken ct) =>
        throw new NotSupportedException();
}
