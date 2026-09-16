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
///
/// <see cref="Calculate"/> is called once per rescue card, always with the same <c>stockRows</c>
/// instance for the whole run (PrioritizeCommand loads it once up front) - re-filtering the full stock
/// list by brand and re-normalizing every candidate row's name (a non-trivial NFD-decomposition
/// operation, see <see cref="ModelNameNormalizer"/>) on every single call was O(cards x stock rows) of
/// pure repeated work. The by-brand, by-normalized-name index built in <see cref="GetOrBuildIndex"/> is
/// built once (keyed by reference equality of the stockRows instance, so it still rebuilds correctly if
/// a caller - e.g. a test - ever passes a different list) and turns every subsequent card's lookup into
/// an O(1) dictionary hit instead of a full rescan.
/// </summary>
public sealed class BundlePriorityCalculator(ModelAliasConfig aliases, BundlePriorityThresholds? thresholds = null)
{
    private readonly BundlePriorityThresholds _thresholds = thresholds ?? BundlePriorityThresholds.Default;

    private IReadOnlyList<VehicleStockRow>? _indexedStockRows;
    private Dictionary<Brand, Dictionary<string, VehicleStockRow>>? _index;

    public PriorityMatchResult Calculate(Brand brand, string? modelName, IReadOnlyList<VehicleStockRow> stockRows)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return new PriorityMatchResult(null, BundlePriority.Unknown, false);
        }

        var brandIndex = GetOrBuildIndex(stockRows).GetValueOrDefault(brand);

        var directMatch = FindByNormalizedName(brandIndex, modelName);
        if (directMatch is not null)
        {
            return new PriorityMatchResult(directMatch.Count, ToPriority(directMatch.Count), false);
        }

        var aliasTarget = aliases.FindKbaModelSeries(brand, modelName);
        if (aliasTarget is not null)
        {
            var aliasMatch = FindByNormalizedName(brandIndex, aliasTarget);
            if (aliasMatch is not null)
            {
                return new PriorityMatchResult(aliasMatch.Count, ToPriority(aliasMatch.Count), true);
            }
        }

        return new PriorityMatchResult(null, BundlePriority.Unknown, false);
    }

    private Dictionary<Brand, Dictionary<string, VehicleStockRow>> GetOrBuildIndex(IReadOnlyList<VehicleStockRow> stockRows)
    {
        if (_index is not null && ReferenceEquals(_indexedStockRows, stockRows))
        {
            return _index;
        }

        var index = new Dictionary<Brand, Dictionary<string, VehicleStockRow>>();
        foreach (var brand in Enum.GetValues<Brand>())
        {
            var byNormalizedName = new Dictionary<string, VehicleStockRow>();
            foreach (var row in stockRows)
            {
                if (!BrandNames.Matches(brand, row.BrandLabel))
                {
                    continue;
                }

                // First-match-wins on a normalized-name collision, same as the original linear
                // FirstOrDefault over stockRows in its original order - shouldn't happen in practice
                // (KBA rows are unique per model series within a brand), but keeps behavior identical
                // if it ever did.
                byNormalizedName.TryAdd(ModelNameNormalizer.Normalize(row.ModelSeries), row);
            }

            index[brand] = byNormalizedName;
        }

        _indexedStockRows = stockRows;
        _index = index;
        return index;
    }

    private static VehicleStockRow? FindByNormalizedName(Dictionary<string, VehicleStockRow>? brandIndex, string modelName) =>
        brandIndex?.GetValueOrDefault(ModelNameNormalizer.Normalize(modelName));

    private BundlePriority ToPriority(int fleetSize) => fleetSize switch
    {
        _ when fleetSize >= _thresholds.High => BundlePriority.High,
        _ when fleetSize >= _thresholds.Medium => BundlePriority.Medium,
        _ => BundlePriority.Low
    };
}
