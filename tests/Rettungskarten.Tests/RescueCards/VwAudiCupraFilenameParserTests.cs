using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Tests.RescueCards;

public class VwAudiCupraFilenameParserTests
{
    [Fact]
    public void Parse_VwFileNameWithVariant_ExtractsAllFields()
    {
        var result = VwAudiCupraFilenameParser.Parse("Volkswagen_Arteon_eHYBRID_Coupe_2020_5d_Hybrid-Electric_DE.pdf");

        Assert.Equal("Arteon", result.ModelName);
        Assert.Equal("eHYBRID", result.Variant);
        Assert.Equal("Coupe", result.BodyType);
        Assert.Equal(2020, result.BuildYearFrom);
        Assert.Equal(5, result.Doors);
        Assert.Equal("Hybrid-Electric", result.FuelType);
        Assert.Equal("DE", result.LanguageCode);
        Assert.Equal(ParseConfidence.High, result.ParseConfidence);
    }

    [Fact]
    public void Parse_AudiFileNameWithDoubleUnderscore_CollapsesEmptyVariantToken()
    {
        var result = VwAudiCupraFilenameParser.Parse(
            "https://emea-dam.audi.com/adobe/assets/urn:aaid:aem:x/original/as/Audi_A6__Stationwagon_2018_5d_GD_EN.pdf");

        Assert.Equal("A6", result.ModelName);
        Assert.Null(result.Variant);
        Assert.Equal("Stationwagon", result.BodyType);
        Assert.Equal(2018, result.BuildYearFrom);
        Assert.Equal(5, result.Doors);
        Assert.Equal("GD", result.FuelType);
        Assert.Equal("EN", result.LanguageCode);
        Assert.Equal(ParseConfidence.High, result.ParseConfidence);
    }

    [Fact]
    public void Parse_CupraFileName_ExtractsModelAndFuelType()
    {
        var result = VwAudiCupraFilenameParser.Parse("Cupra_Terramar__SUV_2024_5d_Hybrid_DE.pdf");

        Assert.Equal("Terramar", result.ModelName);
        Assert.Equal("SUV", result.BodyType);
        Assert.Equal(2024, result.BuildYearFrom);
        Assert.Equal("Hybrid", result.FuelType);
        Assert.Equal("DE", result.LanguageCode);
    }

    [Fact]
    public void Parse_TooFewTokens_ReturnsUnparsedWithoutThrowing()
    {
        var result = VwAudiCupraFilenameParser.Parse("Volkswagen_Golf.pdf");

        Assert.Equal(ParseConfidence.Unparsed, result.ParseConfidence);
        Assert.Null(result.BuildYearFrom);
    }
}
