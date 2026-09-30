using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Placeholder source for Polestar, registered so that the brand reports a reason instead of the
/// generic "no source registered". Polestar does publish German rescue sheets, but only as individual
/// PDFs on its DatoCMS asset host (<c>polestar.com/dato-assets/11286/{timestamp}-{name}.pdf</c>, e.g. a
/// 2020 "rettungsdatenblatter-gesamt.pdf") that no Polestar page links. Checked 2026-09-29, without a
/// headless browser: the polestar.com sitemaps (pages, support, manual) contain no rescue/first-responder
/// page; the German support pages (/de/support/, /de/support/polestar-2/) are client-rendered and their
/// server HTML carries no document links besides a legal notice; the Polestar Tech Hub
/// (polestartechhub.com), which hosts the extrication guides, is a client-rendered app whose robots.txt
/// disallows everything - and those are ERGs, not rescue sheets, anyway. The asset URLs embed an upload
/// timestamp, so they can't be derived either, and hard-coding the handful known today would silently
/// go stale. Discovery therefore throws <see cref="NotSupportedException"/> (brand outcome "not
/// implemented", with this reason) without making any request.
/// </summary>
public sealed class PolestarRescueCardSource(IHttpClientFactory httpClientFactory) : RescueCardSourceBase(httpClientFactory)
{
    public override Brand Brand => Brand.Polestar;

    public override Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct) =>
        throw new NotSupportedException(Strings.Get("RescueCards_Polestar_NoOverviewPage"));
}
