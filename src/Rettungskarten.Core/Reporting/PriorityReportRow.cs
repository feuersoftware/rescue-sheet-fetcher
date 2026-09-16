using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Reporting;

/// <summary>
/// One row of the priority report: one per distinct (Brand, ModelName), not one per rescue card. See
/// <see cref="PriorityReportAggregator"/> for why collapsing to this granularity loses no information
/// the report actually needs. <see cref="NotDownloadedCount"/> is the one thing that *would* be lost
/// silently by a naive collapse - how many of this model's cards have no local PDF yet
/// (Status != Downloaded) - so a reader isn't misled into thinking a "High priority" model is fully
/// covered on disk when some of its variants actually failed to download.
/// </summary>
public sealed record PriorityReportRow(
    Brand Brand, string? ModelName, int CardCount, int NotDownloadedCount, int? EstimatedFleetSize, BundlePriority BundlePriority);

/// <summary>
/// Collapses the full per-card list into one row per (Brand, ModelName). Pure logic, no I/O.
///
/// A model's EstimatedFleetSize/BundlePriority is purely a function of (Brand, ModelName) -
/// BundlePriorityCalculator.Calculate takes no other input that varies between two cards of the same
/// model (different build year, body type, or variant text never changes the KBA match) - so every
/// card sharing a (Brand, ModelName) already has *identical* fleet-size/priority values, verified
/// (not just assumed) below by checking every card in a group agrees before taking the first one's
/// values, rather than trusting the invariant blindly. Before this, the report listed one row per
/// rescue card: a popular model with many year/body-type variants (e.g. "Golf" across 8 different
/// model years) showed up as 8 rows with the same number repeated, which is noise for the report's
/// actual purpose (deciding which *models* are common enough to bundle) - the full per-variant detail
/// is still available in each model's own JSON sidecars under data/rescue-cards/{brand}/{model}/, this
/// report just doesn't need to repeat it.
/// </summary>
public static class PriorityReportAggregator
{
    public static IReadOnlyList<PriorityReportRow> Aggregate(IEnumerable<RescueCardMetadata> cards) =>
        cards
            .GroupBy(c => (c.Brand, c.ModelName))
            .Select(g =>
            {
                var distinctMatches = g.Select(c => (c.EstimatedFleetSize, c.BundlePriority)).Distinct().ToList();
                if (distinctMatches.Count > 1)
                {
                    // Shouldn't happen given BundlePriorityCalculator's inputs (see doc comment above),
                    // but if some future caller ever aggregates cards from runs with different stock
                    // data/aliases, silently picking one card's values would misreport the rest -
                    // surfacing this loudly is safer than a wrong report nobody notices is wrong.
                    throw new InvalidOperationException(
                        $"Cards for {g.Key.Brand}/{g.Key.ModelName} disagree on EstimatedFleetSize/BundlePriority " +
                        $"({distinctMatches.Count} distinct combinations) - are these cards from more than one prioritize run?");
                }

                var (fleetSize, priority) = distinctMatches[0];
                return new PriorityReportRow(
                    Brand: g.Key.Brand,
                    ModelName: g.Key.ModelName,
                    CardCount: g.Count(),
                    NotDownloadedCount: g.Count(c => c.Status != RescueCardStatus.Downloaded),
                    EstimatedFleetSize: fleetSize,
                    BundlePriority: priority);
            })
            .OrderByDescending(r => r.EstimatedFleetSize ?? -1)
            .ThenBy(r => r.ModelName, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
