using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>Cross-brand parsing helpers added for the non-VW brands - label formats below are real
/// ones from the manufacturer pages named in each case.</summary>
public class SharedParsingTests
{
    [Theory]
    [InlineData("CX-5 | KE | 2012 – 2017 | VIN-Bereich", 2012, 2017)] // Mazda, en dash
    [InlineData("RAV4 ab 03/2019", 2019, null)] // Toyota, month before the year
    [InlineData("ASX (ab Modelljahr 2023)", 2023, null)] // Mitsubishi
    [InlineData("Sammel-PDF vor 11/2019", null, 2019)] // Hyundai legacy combined PDF
    [InlineData("i30 03.2017 - 06.2020", 2017, 2020)]
    [InlineData("Model S since 2021", 2021, null)]
    public void ModelYearRange_AdditionalPhrasings(string text, int? from, int? to)
    {
        var range = ModelYearRangeTextHelper.Extract(text);

        Assert.Equal(from, range.From);
        Assert.Equal(to, range.To);
    }

    [Fact]
    public void ModelYearRange_SingleYear_MeaningDependsOnCaller()
    {
        Assert.Equal(new YearRange(2021, 2021), ModelYearRangeTextHelper.Extract("CAPTUR 2 - 2021"));
        Assert.Equal(new YearRange(2021, null), ModelYearRangeTextHelper.Extract("CAPTUR 2 - 2021", singleYearIsStartYear: true));
    }

    [Theory]
    [InlineData("Astra L Kombi (Sports Tourer) Hybrid (2021)", "Astra L", "Sports Tourer", "Hybrid", 2021)]
    [InlineData("Spring (ab 2024)", "Spring", null, null, 2024)]
    [InlineData("Volvo XC60 Typ D 2009-2017", "XC60 Typ D", null, null, 2009)]
    [InlineData("2008 (ab 2019)", "2008", null, null, 2019)] // a model name that looks like a year
    public void LabelParser_ExtractsModelYearAndAttributes(string label, string? model, string? body, string? fuel, int? yearFrom)
    {
        var parsed = RescueSheetLabelParser.Parse(label, new RescueSheetLabelParser.Options(["Volvo", "Land Rover"]));

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(body, parsed.BodyType);
        Assert.Equal(fuel, parsed.FuelType);
        Assert.Equal(yearFrom, parsed.BuildYearFrom);
        Assert.Equal(label, parsed.Variant);
        Assert.Equal("DE", parsed.LanguageCode);
    }

    [Fact]
    public void LabelParser_NoYear_IsUnparsed()
    {
        var parsed = RescueSheetLabelParser.Parse("ZS EV");

        Assert.Equal(ParseConfidence.Unparsed, parsed.ParseConfidence);
    }

    [Fact]
    public void WordMatch_DoesNotFindShortWordsInsideOthers()
    {
        // A plain substring search finds "EV" in "Levante" - the whole-word variant must not.
        Assert.Null(VehicleAttributeTextHelper.ExtractLastWordMatch("Maserati Levante 2016", VehicleAttributeTextHelper.CommonFuelTypes));
        Assert.Equal("EV", VehicleAttributeTextHelper.ExtractLastWordMatch("MG4_EV_2022", VehicleAttributeTextHelper.CommonFuelTypes));
        Assert.Equal("Plug-in-Hybrid", VehicleAttributeTextHelper.ExtractLastWordMatch("Kuga Plug-in-Hybrid", VehicleAttributeTextHelper.CommonFuelTypes));
    }

    [Theory]
    [InlineData("ERG_Peugeot_e-208_de.pdf", RescueDocumentKind.EmergencyResponseGuide)]
    [InlineData("Emergency Response Guide Model Y", RescueDocumentKind.EmergencyResponseGuide)]
    [InlineData("Legende Rettungsdatenblatt", RescueDocumentKind.OtherDocument)]
    [InlineData("Leitfaden für Rettungskräfte", RescueDocumentKind.OtherDocument)]
    [InlineData("Toyota_Yaris_Schragheck_2020_5d_Hybrid_DE.pdf", RescueDocumentKind.RescueSheet)]
    [InlineData("Energy_Bergen_2021.pdf", RescueDocumentKind.RescueSheet)] // "erg" inside words is not "ERG"
    public void DocumentClassifier(string text, RescueDocumentKind expected) =>
        Assert.Equal(expected, RescueDocumentClassifier.Classify(text));

    [Fact]
    public void LanguagePreference_GermanFirst_EnglishOnlyAsFallback_OthersNever()
    {
        var items = new[]
        {
            (Model: "Model 3", Lang: "DE"), (Model: "Model 3", Lang: "EN"),
            (Model: "Model Y", Lang: "EN"), (Model: "Model Y", Lang: "FR"),
            (Model: "Model X", Lang: "FR")
        };

        var result = LanguagePreference.PreferGermanThenEnglish(items, i => i.Model, i => i.Lang);

        Assert.Equal([("Model 3", "DE"), ("Model Y", "EN")], result);
    }

    [Fact]
    public void StandardParser_LocalePairSuffix_IsCollapsed()
    {
        // Servicebox appends a full locale ("_de_DE") - without collapsing it, "de" would land in the
        // fuel-type slot and every field before it would shift by one.
        var parsed = StandardRescueSheetFilenameParser.Parse("Peugeot_208_II_Schragheck_2019_5d_Elektro_de_DE.pdf");

        Assert.Equal("208", parsed.ModelName);
        Assert.Equal("II", parsed.Variant);
        Assert.Equal("Schragheck", parsed.BodyType);
        Assert.Equal(2019, parsed.BuildYearFrom);
        Assert.Equal(5, parsed.Doors);
        Assert.Equal("Elektro", parsed.FuelType);
        Assert.Equal("DE", parsed.LanguageCode);
        Assert.Equal(ParseConfidence.High, parsed.ParseConfidence);
    }

    [Fact]
    public void StandardParser_TryParse_RejectsFreeFormFilenames()
    {
        Assert.False(StandardRescueSheetFilenameParser.TryParse("renault_rettungsdatenblatt_avantime.pdf", out _));
        Assert.True(StandardRescueSheetFilenameParser.TryParse(
            "https://cdn.test/Dacia_Spring%20Electric_Schragheck_2021_5d_Elektro_DE.pdf?v=2", out var parsed));
        Assert.Equal("Spring Electric", parsed.ModelName);
    }
}
