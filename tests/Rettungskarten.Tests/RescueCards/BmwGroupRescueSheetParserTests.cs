using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.BmwGroup;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>Labels/categories/keys below are real AOS API values (2026-09).</summary>
public sealed class BmwGroupRescueSheetParserTests
{
    [Theory]
    [InlineData("7er-Reihe G11/G12 Linkslenker (ab 10/2015)", "G11/G12")]
    [InlineData("7er-Reihe ActiveHybrid 7 F01 / F02 /F04 (seit 07/2012)", "F01/F02/F04")]
    [InlineData("7er-Reihe E65/66 (07/2001 - 07/2008)", "E65/66")]
    [InlineData("Z3 Roadster E36/7 (09/1995 - 06/2002)", "E36/7")]
    [InlineData("X1 F48PHEV (ab 03/2020)", "F48")]
    [InlineData("i3 NA0 BEV (seit 08/2026)", "NA0")]
    [InlineData("I4 G26 BEVE (seit 11/2021)", "G26")] // "I4" is a model name, not a chassis code
    [InlineData("BMW ActiveE (seit 09/2011)", null)]
    public void ExtractChassisCode_Bmw(string label, string? expected) =>
        Assert.Equal(expected, BmwGroupRescueSheetParser.ExtractChassisCode(Brand.BMW, label));

    [Theory]
    [InlineData("Rolls-Royce Dawn Drophead Coupe (RR 6) (seit 02/2016)", "RR6")]
    [InlineData("Rolls-Royce Dawn(RR06)(seit 02/2016)", "RR6")]
    [InlineData("Rolls Royce Spectre RR25 BEVE (seit 08/2023)", "RR25")]
    public void ExtractChassisCode_RollsRoyce(string label, string expected) =>
        Assert.Equal(expected, BmwGroupRescueSheetParser.ExtractChassisCode(Brand.RollsRoyce, label));

    [Fact]
    public void Parse_LabelWithoutDate_KeepsVariantAndLowersConfidence()
    {
        var parsed = BmwGroupRescueSheetParser.Parse(
            Brand.Mini, "MINI Cooper SE F56 BEV", "coupe", "f56", "Rescue-information/mini/coupe/f56/VUL-REK-P-MINI_F56BN_de-DE.pdf");

        Assert.Equal("MINI Cooper SE F56 BEV", parsed.Variant);
        Assert.Null(parsed.BuildYearFrom);
        Assert.Equal(ParseConfidence.Heuristic, parsed.ParseConfidence);
    }

    [Fact]
    public void Parse_EnDashRange_AndVariantStripsDate()
    {
        var parsed = BmwGroupRescueSheetParser.Parse(
            Brand.Mini, "MINI R56 (09/2006 – 02/2014)", "coupe", "r56", "Rescue-information/mini/coupe/r56/de_MINI-R56.pdf");

        Assert.Equal("MINI", parsed.ModelName);
        Assert.Equal("MINI R56", parsed.Variant);
        Assert.Equal(2006, parsed.BuildYearFrom);
        Assert.Equal(2014, parsed.BuildYearTo);
        Assert.Equal("Schrägheck", parsed.BodyType);
    }

    [Fact]
    public void Parse_SingleDateWithoutPrefix_IsStartYear()
    {
        var parsed = BmwGroupRescueSheetParser.Parse(
            Brand.BMW, "5er-Reihe G60 ICE (07/2023)", "sedan", "5-series", "Rescue-information/BMW/sedan/5-series/VUL-REK-P-G60_ICE_de-DE.pdf");

        Assert.Equal("5er", parsed.ModelName);
        Assert.Equal(2023, parsed.BuildYearFrom);
        Assert.Null(parsed.BuildYearTo);
        Assert.Equal("ICE", parsed.FuelType);
        Assert.Equal("Limousine", parsed.BodyType);
    }

    [Fact]
    public void Parse_HybridElectricFilename_IsPhev()
    {
        var parsed = BmwGroupRescueSheetParser.Parse(
            Brand.BMW, "X1 U11 (ab 07/2022)", "suv", "x1",
            "Rescue-information/BMW/suv/x1/VUL-BMW_X1 Series_U11_SUV_2022_5d_Hybrid (Electric)__de-DE.pdf");

        Assert.Equal("PHEV", parsed.FuelType);
        Assert.Equal(5, parsed.Doors);
    }
}
