using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Stellantis;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>Labels, filenames and pictograms below are verbatim from the real Servicebox pages.</summary>
public class ServiceboxLabelParserTests
{
    [Theory]
    [InlineData("208 (1PIA) 2012→", "208", false)]
    [InlineData("e208 (1PP2) 2019→", "208", true)]
    [InlineData("ë-C5 Aircross Bev (1CRE) 2025→", "C5 Aircross", true)]
    [InlineData("E-Expert Hydrogen électrique Fuel Cell (2PK0) lieferwagen 2021→", "Expert", true)]
    [InlineData("e- SpaceTourer (1CK0) 2020→", "SpaceTourer", true)]
    [InlineData("307 SW et Break (1PT5) 2002→", "307", false)]
    [InlineData("DS 7 CROSSBACK E-TENSE Hybride (1SX8) 2019→", "DS 7 CROSSBACK", false)]
    [InlineData("108 (3-Türer) (1PB1) 2014→", "108", false)]
    [InlineData("Partner Origin (1PM5) verglaster kastenwagen 1996→", "Partner", false)]
    [InlineData("3008 Hybrid4 (1PPD) 2020 →", "3008", false)]
    public void ExtractModelName_StripsPowertrainBodyAndTrimWords(string label, string expectedModel, bool expectedElectric)
    {
        var (model, electric) = ServiceboxLabelParser.ExtractModelName(label);

        Assert.Equal(expectedModel, model);
        Assert.Equal(expectedElectric, electric);
    }

    [Fact]
    public void Parse_RowLabel_ReadsProjectCodeYearAndPictogram()
    {
        var parsed = ServiceboxLabelParser.Parse("206 SW (1PT1) 2002→", "FAD_206_SW_1PT1_de_DE.pdf", "Essence-Diesel_01.png", "DE");

        Assert.Equal("206", parsed.ModelName);
        Assert.Equal("1PT1", parsed.ChassisCode);
        Assert.Equal(2002, parsed.BuildYearFrom);
        Assert.Null(parsed.BuildYearTo);
        Assert.Equal("Kombi", parsed.BodyType);
        Assert.Equal("Benzin/Diesel", parsed.FuelType);
        Assert.Equal(ParseConfidence.High, parsed.ParseConfidence);
    }

    [Fact]
    public void Parse_MildHybridLabelledPlugIn_PrefersMhev()
    {
        var parsed = ServiceboxLabelParser.Parse(
            "C5 Aircross Hybrid PLUG IN Mhev (1CRE) 2025→", "FAD_C5_AIRCROSS_Hybrid_1CRE_2025_MHEV_de_DE.pdf", "Hybride_1.png", "DE");

        Assert.Equal("Mild-Hybrid", parsed.FuelType);
    }

    [Fact]
    public void Parse_LabelWithoutYear_TakesYearFromFileNameButNotTheModelNumber()
    {
        var parsed = ServiceboxLabelParser.Parse("e-Berlingo (5 Portes) (1CK9)", "FAD_eBerlingo_5_places_2021_1CK9_de_DE.pdf", null, "DE");

        Assert.Equal("Berlingo", parsed.ModelName);
        Assert.Equal(2021, parsed.BuildYearFrom);
        Assert.Equal("Elektro", parsed.FuelType);
        Assert.Null(parsed.Doors); // "5 places" are seats, not doors

        var peugeot2008 = ServiceboxLabelParser.Parse("2008 (1PP1)", "2008_(1PP1)_2023_de.pdf", null, "DE");
        Assert.Equal(2023, peugeot2008.BuildYearFrom);
    }

    [Theory]
    [InlineData("DS 3 CROSSBACK E-TENSE (1SD3) 2019→", "Elektro")]
    [InlineData("DS 7 CROSSBACK E-TENSE Hybride (1SX8) 2019→", "Hybrid")]
    public void Parse_DsETense_IsElectricUnlessLabelledHybride(string label, string expectedFuel) =>
        Assert.Equal(expectedFuel, ServiceboxLabelParser.Parse(label, "x_de_DE.pdf", null, "DE").FuelType);

    [Fact]
    public void Parse_GuidPictogramAndNoFuelWords_LeavesFuelTypeEmpty()
    {
        var parsed = ServiceboxLabelParser.Parse("C3 (1CA8) 2002→", "FAD_C3_1CA8_de_DE.pdf", "0e538be0-69f5-47de-ac2f-37da9b3c0344..png", "DE");

        Assert.Null(parsed.FuelType);
    }

    [Fact]
    public void Parse_DoorsFromNewFileNames()
    {
        var parsed = ServiceboxLabelParser.Parse("ë-C3 (1CSC) 2024→", "FAD_e-C3_1CSC_2024_5d_Electric_de.pdf", "Electrique_01.png", "DE");

        Assert.Equal(5, parsed.Doors);
        Assert.Equal("C3", parsed.ModelName);
        Assert.Equal("Elektro", parsed.FuelType);
    }
}
