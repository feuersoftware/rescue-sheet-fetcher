using Rettungskarten.Core.Config;
using Rettungskarten.Core.Matching;
using Rettungskarten.Core.Models;
using Rettungskarten.Core.Priority;
using Rettungskarten.Infrastructure.Stock;

namespace Rettungskarten.Tests.Matching;

/// <summary>Brand -> group/parent/KBA-label mapping, checked against the real fz12_2026.xlsx where the
/// claim is about KBA's own labels.</summary>
public class BrandMappingTests
{
    private static readonly Lazy<IReadOnlyList<VehicleStockRow>> StockRows = new(() =>
        KbaStockXlsxParser.Parse(File.ReadAllBytes(Path.Combine("Fixtures", "fz12_2026.xlsx")), 2026, "https://example.test").Rows);

    [Theory]
    [InlineData(Brand.Cupra, Brand.Seat)]
    [InlineData(Brand.MercedesAmg, Brand.MercedesBenz)]
    [InlineData(Brand.MercedesEq, Brand.MercedesBenz)]
    [InlineData(Brand.Maybach, Brand.MercedesBenz)]
    [InlineData(Brand.Vauxhall, Brand.Opel)]
    [InlineData(Brand.FiatProfessional, Brand.Fiat)]
    [InlineData(Brand.Abarth, Brand.Fiat)]
    public void SubBrands_HaveTheirKbaParent(Brand brand, Brand parent) =>
        Assert.Equal(parent, BrandGroups.ParentBrandOf(brand));

    [Theory]
    [InlineData(Brand.VW, ManufacturerGroup.VolkswagenGroup)]
    [InlineData(Brand.Smart, ManufacturerGroup.MercedesBenzGroup)]
    [InlineData(Brand.Mini, ManufacturerGroup.BmwGroup)]
    [InlineData(Brand.Vauxhall, ManufacturerGroup.Stellantis)]
    [InlineData(Brand.Daihatsu, ManufacturerGroup.ToyotaGroup)]
    [InlineData(Brand.Polestar, ManufacturerGroup.Geely)]
    [InlineData(Brand.LandRover, ManufacturerGroup.TataMotors)]
    [InlineData(Brand.Cadillac, ManufacturerGroup.GeneralMotors)]
    [InlineData(Brand.Maxus, ManufacturerGroup.SaicMotor)]
    [InlineData(Brand.Ford, ManufacturerGroup.Independent)]
    [InlineData(Brand.Nissan, ManufacturerGroup.Independent)]
    public void GroupOf(Brand brand, ManufacturerGroup expected) =>
        Assert.Equal(expected, BrandGroups.GroupOf(brand));

    [Fact]
    public void EntryGroupOverride_WinsOverTheBrandDefault()
    {
        var parsed = new ParsedModelInfo("#1", null, null, 2022, null, null, null, "DE", ParseConfidence.Heuristic);

        Assert.Equal(ManufacturerGroup.MercedesBenzGroup, new RescueCardEntry(Brand.Smart, "u", "d", "r", parsed).ManufacturerGroup);
        Assert.Equal(ManufacturerGroup.Geely,
            new RescueCardEntry(Brand.Smart, "u", "d", "r", parsed, ManufacturerGroupOverride: ManufacturerGroup.Geely).ManufacturerGroup);
    }

    [Theory]
    [InlineData("ALFA ROMEO GIULIA", "ALFA ROMEO", "GIULIA")]
    [InlineData("LAND ROVER RANGE ROVER SPORT", "LAND ROVER", "RANGE ROVER SPORT")]
    [InlineData("LYNK & CO 01", "LYNK & CO", "01")]
    [InlineData("MG ROEWE ZS", "MG ROEWE", "ZS")]
    [InlineData("MERCEDES AMG GT", "MERCEDES", "AMG GT")]
    [InlineData("VW GOLF", "VW", "GOLF")]
    public void KbaSplit_MultiWordBrandLabels(string modellreihe, string brand, string model) =>
        Assert.Equal((brand, model), KbaStockXlsxParser.SplitBrandAndModel(modellreihe));

    [Theory]
    [InlineData(Brand.AlfaRomeo, "GIULIA")]
    [InlineData(Brand.LandRover, "DEFENDER")]
    [InlineData(Brand.MG, "ZS")]
    [InlineData(Brand.KGM, "TORRES")]
    [InlineData(Brand.KGM, "KORANDO")] // listed under SSANGYONG in the previous year's column, KGM now
    [InlineData(Brand.MercedesBenz, "A-KLASSE")]
    [InlineData(Brand.MercedesAmg, "AMG GT")] // counted as "MERCEDES AMG GT"
    [InlineData(Brand.Citroen, "C3")]
    [InlineData(Brand.Smart, "FORTWO")]
    [InlineData(Brand.Mini, "MINI")]
    [InlineData(Brand.Tesla, "MODEL 3")]
    public void RealStockRows_MatchTheBrand(Brand brand, string modelSeries)
    {
        var calculator = new BundlePriorityCalculator(ModelAliasConfig.Empty);

        var result = calculator.Calculate(brand, modelSeries, StockRows.Value);

        Assert.NotNull(result.EstimatedFleetSize);
    }

    [Fact]
    public void RealStockRows_ModelInSeveralSegments_IsSummed()
    {
        // "LAND ROVER DEFENDER" is listed under both SUVs and utilities; the fleet is the sum.
        var rows = StockRows.Value.Where(r => r.BrandLabel == "LAND ROVER" && r.ModelSeries == "DEFENDER").ToList();
        Assert.True(rows.Count >= 2);

        var result = new BundlePriorityCalculator(ModelAliasConfig.Empty).Calculate(Brand.LandRover, "Defender", StockRows.Value);

        Assert.Equal(rows.Sum(r => r.Count), result.EstimatedFleetSize);
    }

    [Fact]
    public void CommaSeparatedSeries_MatchesEachPart_ButAnExactSeriesWins()
    {
        var rows = new[]
        {
            new VehicleStockRow("SUVS", "MERCEDES", "GLK, GLC", 300_000),
            new VehicleStockRow("SUVS", "MERCEDES", "GLK", 5_000)
        };
        var calculator = new BundlePriorityCalculator(ModelAliasConfig.Empty);

        Assert.Equal(300_000, calculator.Calculate(Brand.MercedesBenz, "GLC", rows).EstimatedFleetSize);
        Assert.Equal(5_000, calculator.Calculate(Brand.MercedesBenz, "GLK", rows).EstimatedFleetSize);
    }

    [Fact]
    public void WildcardAlias_AppliesToEveryModelOfTheBrand_ExactAliasWins()
    {
        var rows = new[]
        {
            new VehicleStockRow("KLEINWAGEN", "MINI", "MINI", 587_315),
            new VehicleStockRow("SUVS", "MINI", "COUNTRYMAN", 50_000)
        };
        var aliases = new ModelAliasConfig([
            new ModelAlias(Brand.Mini, ModelAliasConfig.AnyModel, "MINI"),
            new ModelAlias(Brand.Mini, "Countryman F60", "COUNTRYMAN")
        ]);
        var calculator = new BundlePriorityCalculator(aliases);

        Assert.Equal(587_315, calculator.Calculate(Brand.Mini, "Cooper SE", rows).EstimatedFleetSize);
        Assert.Equal(50_000, calculator.Calculate(Brand.Mini, "Countryman F60", rows).EstimatedFleetSize);
    }

    [Fact]
    public void BrandsNotListedByKba_AreDeclaredSo()
    {
        Assert.False(BrandNames.IsListedInKbaStock(Brand.RollsRoyce));
        Assert.DoesNotContain(StockRows.Value, r => BrandNames.Matches(Brand.RollsRoyce, r.BrandLabel));
        Assert.True(BrandNames.IsListedInKbaStock(Brand.VW));
    }
}
