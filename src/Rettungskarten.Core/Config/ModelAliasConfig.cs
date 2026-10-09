using Rettungskarten.Core.Matching;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Config;

/// <summary>
/// Hand-curated overrides for cases where a rescue card's model name doesn't automatically match the
/// KBA FZ12 "Modellreihe" spelling (e.g. "ID.4" vs. "ID 4", "T-Roc" vs. "TROC"). A
/// <see cref="ModelAlias.ModelName"/> of <see cref="AnyModel"/> ("*") is a brand-wide fallback for
/// brands KBA only lists as a single series (e.g. every Mini model is counted as "MINI MINI"); an
/// exact model alias always wins over it.
/// </summary>
public sealed record ModelAlias(Brand Brand, string ModelName, string KbaModelSeries);

public sealed record ModelAliasConfig(IReadOnlyList<ModelAlias> Aliases)
{
    public const string AnyModel = "*";

    public string? FindKbaModelSeries(Brand brand, string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return null;
        }

        var normalized = ModelNameNormalizer.Normalize(modelName);
        var brandAliases = Aliases.Where(a => a.Brand == brand).ToList();

        return brandAliases
                .Where(a => a.ModelName != AnyModel && ModelNameNormalizer.Normalize(a.ModelName) == normalized)
                .Select(a => a.KbaModelSeries)
                .FirstOrDefault()
            ?? brandAliases.Where(a => a.ModelName == AnyModel).Select(a => a.KbaModelSeries).FirstOrDefault();
    }

    public static readonly ModelAliasConfig Empty = new([]);
}
