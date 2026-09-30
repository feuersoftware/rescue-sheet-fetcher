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
///
/// Two FZ12 layout facts shape the index (both verified against fz12_2026.xlsx):
/// - The same model series appears once per segment it is registered in - vans and utilities are
///   split across "GROSSRAUM-VANS", "UTILITIES" and "WOHNMOBILE" (e.g. "VW CADDY", "MERCEDES VITO",
///   "FIAT DUCATO", "LAND ROVER DEFENDER" in both SUVs and utilities). The model's fleet is the sum of
///   those rows; taking only the first one undercounted every such model.
/// - Some series name several models at once, comma-separated ("MERCEDES GLK, GLC", "ML-KLASSE, GLE",
///   "FORD TRANSIT, TOURNEO"). Each part is indexed as a fallback name for the whole row, so a "GLC"
///   card matches without an alias; an exact series name always wins over such a part.
/// </summary>
public sealed class BundlePriorityCalculator(ModelAliasConfig aliases, BundlePriorityThresholds? thresholds = null)
{
    private readonly BundlePriorityThresholds _thresholds = thresholds ?? BundlePriorityThresholds.Default;

    private IReadOnlyList<VehicleStockRow>? _indexedStockRows;
    private Dictionary<Brand, BrandIndex>? _index;

    public PriorityMatchResult Calculate(Brand brand, string? modelName, IReadOnlyList<VehicleStockRow> stockRows)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return new PriorityMatchResult(null, BundlePriority.Unknown, false);
        }

        var brandIndex = GetOrBuildIndex(stockRows).GetValueOrDefault(brand);

        var directMatch = brandIndex?.Find(modelName);
        if (directMatch is { } directCount)
        {
            return new PriorityMatchResult(directCount, ToPriority(directCount), false);
        }

        var aliasTarget = aliases.FindKbaModelSeries(brand, modelName);
        if (aliasTarget is not null && brandIndex?.Find(aliasTarget) is { } aliasCount)
        {
            return new PriorityMatchResult(aliasCount, ToPriority(aliasCount), true);
        }

        return new PriorityMatchResult(null, BundlePriority.Unknown, false);
    }

    private Dictionary<Brand, BrandIndex> GetOrBuildIndex(IReadOnlyList<VehicleStockRow> stockRows)
    {
        if (_index is not null && ReferenceEquals(_indexedStockRows, stockRows))
        {
            return _index;
        }

        var index = new Dictionary<Brand, BrandIndex>();
        foreach (var brand in Enum.GetValues<Brand>())
        {
            var brandIndex = new BrandIndex();
            foreach (var row in stockRows)
            {
                if (BrandNames.Matches(brand, row.BrandLabel))
                {
                    brandIndex.Add(row);
                }
            }

            index[brand] = brandIndex;
        }

        _indexedStockRows = stockRows;
        _index = index;
        return index;
    }

    private BundlePriority ToPriority(int fleetSize) => fleetSize switch
    {
        _ when fleetSize >= _thresholds.High => BundlePriority.High,
        _ when fleetSize >= _thresholds.Medium => BundlePriority.Medium,
        _ => BundlePriority.Low
    };

    private sealed class BrandIndex
    {
        private readonly Dictionary<string, int> _bySeriesName = new();
        private readonly Dictionary<string, int> _bySeriesNamePart = new();

        public void Add(VehicleStockRow row)
        {
            var name = ModelNameNormalizer.Normalize(row.ModelSeries);
            _bySeriesName[name] = _bySeriesName.GetValueOrDefault(name) + row.Count;

            var parts = row.ModelSeries.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                return;
            }

            foreach (var part in parts)
            {
                var partName = ModelNameNormalizer.Normalize(part);
                _bySeriesNamePart[partName] = _bySeriesNamePart.GetValueOrDefault(partName) + row.Count;
            }
        }

        public int? Find(string modelName)
        {
            var normalized = ModelNameNormalizer.Normalize(modelName);
            if (_bySeriesName.TryGetValue(normalized, out var count))
            {
                return count;
            }

            return _bySeriesNamePart.TryGetValue(normalized, out var partCount) ? partCount : null;
        }
    }
}
