using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Every brand's combined-PDF layout known to `split` - one entry per brand that publishes (some of)
/// its rescue sheets only as a combined document. A brand listed here gets `split &lt;brand&gt;`
/// support automatically; `split all` runs every one of them.
/// </summary>
public static class CombinedPdfLayouts
{
    public static IReadOnlyList<ICombinedPdfLayout> All { get; } =
    [
        new PorscheCombinedPdfLayout(),
        new MaseratiCombinedPdfLayout(),
        new DaihatsuCombinedPdfLayout(),
        new SubaruCombinedPdfLayout(),
        new HyundaiCombinedPdfLayout(),
        new KiaCombinedPdfLayout(),
        new NissanCombinedPdfLayout(),
        new FordCombinedPdfLayout()
    ];

    public static IReadOnlyList<ICombinedPdfLayout> For(Brand brand) =>
        All.Where(l => l.Brand == brand).ToList();
}
