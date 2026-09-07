using Rettungskarten.Core.Config;
using Rettungskarten.Core.Matching;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Priority;

public sealed record BundlePriorityThresholds(int High = 100_000, int Medium = 20_000)
{
    public static readonly BundlePriorityThresholds Default = new();
}

public sealed record PriorityMatchResult(int? EstimatedFleetSize, BundlePriority Priority, bool MatchedViaAlias);

/// <summary>
/// Matches a rescue card's model name against KBA FZ12 stock rows to estimate how common the model
/// actually is in Germany, and derives a bundle/on-demand priority tier from it. Pure logic, no I/O:
/// the actual stock rows and alias config are loaded and passed in by the caller.
/// </summary>
public sealed class BundlePriorityCalculator(ModelAliasConfig aliases, BundlePriorityThresholds? thresholds = null)
{
    private readonly BundlePriorityThresholds _thresholds = thresholds ?? BundlePriorityThresholds.Default;

    public PriorityMatchResult Calculate(Brand brand, string? modelName, IReadOnlyList<VehicleStockRow> stockRows)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return new PriorityMatchResult(null, BundlePriority.Unknown, false);
        }

        var brandRows = stockRows.Where(r => BrandNames.Matches(brand, r.BrandLabel)).ToList();

        var directMatch = FindByNormalizedName(brandRows, modelName);
        if (directMatch is not null)
        {
            return new PriorityMatchResult(directMatch.Count, ToPriority(directMatch.Count), false);
        }

        var aliasTarget = aliases.FindKbaModelSeries(brand, modelName);
        if (aliasTarget is not null)
        {
            var aliasMatch = FindByNormalizedName(brandRows, aliasTarget);
            if (aliasMatch is not null)
            {
                return new PriorityMatchResult(aliasMatch.Count, ToPriority(aliasMatch.Count), true);
            }
        }

        return new PriorityMatchResult(null, BundlePriority.Unknown, false);
    }

    private static VehicleStockRow? FindByNormalizedName(IReadOnlyList<VehicleStockRow> rows, string modelName)
    {
        var normalized = ModelNameNormalizer.Normalize(modelName);
        return rows.FirstOrDefault(r => ModelNameNormalizer.Normalize(r.ModelSeries) == normalized);
    }

    private BundlePriority ToPriority(int fleetSize) => fleetSize switch
    {
        _ when fleetSize >= _thresholds.High => BundlePriority.High,
        _ when fleetSize >= _thresholds.Medium => BundlePriority.Medium,
        _ => BundlePriority.Low
    };
}
