using Rettungskarten.Core.Config;
using Rettungskarten.Core.Models;
using Rettungskarten.Core.Priority;

namespace Rettungskarten.Tests.Priority;

public class BundlePriorityCalculatorTests
{
    private static readonly VehicleStockRow[] StockRows =
    [
        new("KOMPAKTKLASSE", "VW", "GOLF", 3_231_990),
        new("KLEINWAGEN", "SEAT", "IBIZA", 408_155),
        new("MITTELKLASSE SUVS", "SKODA", "KAROQ", 50_000),
        new("MINIS", "VW", "UP", 15_000),
        new("KOMPAKTKLASSE SUVS", "SEAT", "FORMENTOR", 120_000)
    ];

    [Fact]
    public void Calculate_DirectMatch_ReturnsHighPriorityForPopularModel()
    {
        var calculator = new BundlePriorityCalculator(ModelAliasConfig.Empty);

        var result = calculator.Calculate(Brand.VW, "Golf", StockRows);

        Assert.Equal(3_231_990, result.EstimatedFleetSize);
        Assert.Equal(BundlePriority.High, result.Priority);
        Assert.False(result.MatchedViaAlias);
    }

    [Fact]
    public void Calculate_LowVolumeModel_ReturnsLowPriority()
    {
        var calculator = new BundlePriorityCalculator(ModelAliasConfig.Empty);

        var result = calculator.Calculate(Brand.VW, "Up", StockRows);

        Assert.Equal(15_000, result.EstimatedFleetSize);
        Assert.Equal(BundlePriority.Low, result.Priority);
    }

    [Fact]
    public void Calculate_NoMatchAndNoAlias_ReturnsUnknown()
    {
        var calculator = new BundlePriorityCalculator(ModelAliasConfig.Empty);

        var result = calculator.Calculate(Brand.VW, "ID.4", StockRows);

        Assert.Null(result.EstimatedFleetSize);
        Assert.Equal(BundlePriority.Unknown, result.Priority);
    }

    [Fact]
    public void Calculate_UsesAliasWhenDirectMatchFails()
    {
        var aliases = new ModelAliasConfig([new ModelAlias(Brand.Skoda, "Karoq Sportline", "KAROQ")]);
        var calculator = new BundlePriorityCalculator(aliases);

        var result = calculator.Calculate(Brand.Skoda, "Karoq Sportline", StockRows);

        Assert.Equal(50_000, result.EstimatedFleetSize);
        Assert.Equal(BundlePriority.Medium, result.Priority);
        Assert.True(result.MatchedViaAlias);
    }

    [Fact]
    public void Calculate_Cupra_MatchesSeatBrandedStockRows()
    {
        // KBA's FZ12 does not track Cupra as its own brand - every Cupra model is counted under
        // "SEAT" instead (see BrandNames' Cupra alias comment). Without that alias this returned
        // Unknown for every single Cupra card regardless of how common the model actually is.
        var calculator = new BundlePriorityCalculator(ModelAliasConfig.Empty);

        var result = calculator.Calculate(Brand.Cupra, "Formentor", StockRows);

        Assert.Equal(120_000, result.EstimatedFleetSize);
        Assert.Equal(BundlePriority.High, result.Priority);
    }

    [Fact]
    public void Calculate_DoesNotMatchAcrossDifferentBrand()
    {
        var calculator = new BundlePriorityCalculator(ModelAliasConfig.Empty);

        // "Golf" exists for VW but not for Audi - must not cross-match.
        var result = calculator.Calculate(Brand.Audi, "Golf", StockRows);

        Assert.Equal(BundlePriority.Unknown, result.Priority);
    }
}
