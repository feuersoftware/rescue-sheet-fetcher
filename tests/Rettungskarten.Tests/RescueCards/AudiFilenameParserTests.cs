using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Tests.RescueCards;

public class AudiFilenameParserTests
{
    [Fact]
    public void Parse_RegularFileName_ExtractsAllFields()
    {
        var result = AudiFilenameParser.Parse(
            "https://emea-dam.audi.com/adobe/assets/urn:aaid:aem:x/original/as/Audi_A6__Stationwagon_2018_5d_GD_EN.pdf");

        Assert.Equal("A6", result.ModelName);
        Assert.Null(result.Variant);
        Assert.Equal("Stationwagon", result.BodyType);
        Assert.Equal(2018, result.BuildYearFrom);
        Assert.Null(result.BuildYearTo);
        Assert.Equal(5, result.Doors);
        Assert.Equal("GD", result.FuelType);
        Assert.Equal("EN", result.LanguageCode);
        Assert.Equal(ParseConfidence.High, result.ParseConfidence);
    }

    [Fact]
    public void Parse_YearRange_ExtractsBothBuildYears()
    {
        var result = AudiFilenameParser.Parse("Audi_A4__Stationwagon_2016-2024_5d_GD_DE.pdf");

        Assert.Equal(2016, result.BuildYearFrom);
        Assert.Equal(2024, result.BuildYearTo);
        Assert.Equal(ParseConfidence.High, result.ParseConfidence);
    }

    // Real filenames found during a production run: Audi's CMS occasionally writes a fuel type as two
    // underscore-separated tokens ("Hybrid_(Electric)") instead of one ("Hybrid-Electric"). Left
    // unhandled, this shifts every field from body type onward by one position - e.g. the model year
    // silently ends up stored as the body type. This must be fixed by merging the parenthesized token
    // back into the fuel type, not just detected after the fact.
    [Theory]
    [InlineData(
        "Audi_A6_Limousine_2018_4d_Hybrid_(Electric)_DE.pdf",
        "A6", "Limousine", 2018, null, 4, "Hybrid (Electric)")]
    [InlineData(
        "Audi_A8_Limousine_2019_4d_Hybrid_(Electric)_DE.pdf",
        "A8", "Limousine", 2019, null, 4, "Hybrid (Electric)")]
    [InlineData(
        "Audi_A7_Limousine_2019-2025_5d_Hybrid_(Electric)_DE.pdf",
        "A7", "Limousine", 2019, 2025, 5, "Hybrid (Electric)")]
    public void Parse_SplitFuelTypeToken_MergesParenthesizedContinuation(
        string fileName, string expectedModel, string expectedBodyType, int expectedYearFrom,
        int? expectedYearTo, int expectedDoors, string expectedFuelType)
    {
        var result = AudiFilenameParser.Parse(fileName);

        Assert.Equal(expectedModel, result.ModelName);
        Assert.Equal(expectedBodyType, result.BodyType);
        Assert.Equal(expectedYearFrom, result.BuildYearFrom);
        Assert.Equal(expectedYearTo, result.BuildYearTo);
        Assert.Equal(expectedDoors, result.Doors);
        Assert.Equal(expectedFuelType, result.FuelType);
        Assert.Equal(ParseConfidence.High, result.ParseConfidence);
    }

    // Real filename found during a production run: a trailing numeral right before the language code,
    // almost certainly a de-duplication suffix from Audi's asset CMS rather than real data (fuel type
    // is never a bare number). Without dropping it, BuildYearFrom/BodyType end up shifted and wrong.
    [Fact]
    public void Parse_TrailingNumeralBeforeLanguageCode_IsDroppedAsCmsDuplicateSuffix()
    {
        var result = AudiFilenameParser.Parse("Audi_e-tron_Sportback_SUV_2019-2023_5d_Electric_1_DE.pdf");

        Assert.Equal("e-tron", result.ModelName);
        Assert.Equal("Sportback", result.Variant);
        Assert.Equal("SUV", result.BodyType);
        Assert.Equal(2019, result.BuildYearFrom);
        Assert.Equal(2023, result.BuildYearTo);
        Assert.Equal(5, result.Doors);
        Assert.Equal("Electric", result.FuelType);
        Assert.Equal("DE", result.LanguageCode);
        Assert.Equal(ParseConfidence.High, result.ParseConfidence);
    }
}
