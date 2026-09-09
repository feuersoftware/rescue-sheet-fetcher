using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Abstractions;

/// <summary>
/// One implementation per brand. Discovery and download are separate steps: discovery is cheap
/// (one page/JSON fetch) and must never throw for a single model's parsing trouble; download is the
/// step that can fail per-model (e.g. Cupra AT's auth-gateway redirect) and reports failure via the
/// result, not an exception. A brand with no working source yet throws
/// <see cref="NotSupportedException"/> from <see cref="DiscoverAsync"/> so the orchestrator can
/// record it as "not implemented" rather than a failure.
/// </summary>
public interface IRescueCardSource
{
    Brand Brand { get; }

    Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct);

    Task<RescueCardDownloadResult> DownloadAsync(RescueCardEntry entry, CancellationToken ct);
}
