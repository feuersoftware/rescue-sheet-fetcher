using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Stellantis;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>Real labels/filenames from the German ex-FCA brand pages (2026-09).</summary>
public class StellantisRescueSheetLabelParserTests
{
    [Theory]
    [InlineData("Jeep® Grand Cherokee 4xe (PHEV)", "x.pdf", "Jeep", "Grand Cherokee", "Plug-in-Hybrid")]
    [InlineData("Jeep® Avenger Benziner", "x.pdf", "Jeep", "Avenger", "Benzin")]
    [InlineData("Jeep® Renegade e-Hybrid", "x.pdf", "Jeep", "Renegade", "Hybrid")]
    [InlineData("Tonale Ibrida Plug-in", "x.pdf", "Alfa Romeo", "Tonale", "Plug-in-Hybrid")]
    [InlineData("Junior Elettrica", "x.pdf", "Alfa Romeo", "Junior", "Elektro")]
    [InlineData("E-Ducato", "x.pdf", "Fiat", "Ducato", "Elektro")]
    [InlineData("Fiat 500 Hybrid", "x.pdf", "Fiat", "500", "Hybrid")]
    [InlineData("Lancia Ypsilon 2011 Natural Power", "x.pdf", "Lancia", "Ypsilon", "Erdgas")]
    [InlineData("Giulia Quadrifoglio", "x.pdf", "Alfa Romeo", "Giulia", null)]
    [InlineData("Abarth 695  Tributo Ferrari", "x.pdf", "Abarth", "695", null)]
    [InlineData("Abarth GRANDE PUNTO", "x.pdf", "Abarth", "Grande Punto", null)]
    [InlineData("Alfa Romeo GT", "x.pdf", "Alfa Romeo", "GT", null)]
    public void Parse_ModelNameAndFuel(string label, string fileName, string brandPrefix, string model, string? fuel)
    {
        var parsed = StellantisRescueSheetLabelParser.Parse(label, fileName, [brandPrefix]);

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(fuel, parsed.FuelType);
        Assert.Equal(ParseConfidence.Heuristic, parsed.ParseConfidence);
        Assert.Equal("DE", parsed.LanguageCode);
    }

    [Theory]
    [InlineData("Journey ECO MY 2009", "Journey", 2009, null)]
    [InlineData("Caliber MY 2006-2011", "Caliber", 2006, 2011)]
    [InlineData("Nitro MY 2007", "Nitro", 2007, null)]
    public void Parse_DodgeModelYearPrefix(string label, string model, int from, int? to)
    {
        var parsed = StellantisRescueSheetLabelParser.Parse(label, "x.pdf", ["Dodge"]);

        Assert.Equal((model, from, to), (parsed.ModelName, parsed.BuildYearFrom!.Value, parsed.BuildYearTo));
    }

    [Fact]
    public void Parse_DoorsPhraseIsNotPartOfTheModelName()
    {
        var parsed = StellantisRescueSheetLabelParser.Parse("Jeep Wrangler 2-Türer", "57_684_WRANGLERJL_18A_JL_002_DE_01_09_18_T.pdf", ["Jeep"]);

        Assert.Equal("Wrangler", parsed.ModelName);
        Assert.Equal(2, parsed.Doors);
        Assert.Equal("JL", parsed.ChassisCode);
    }

    [Fact]
    public void Parse_DocumentDateInFilenameIsNotABuildYear()
    {
        var parsed = StellantisRescueSheetLabelParser.Parse("Fiat 600", "365_FIAT_DE_01_06_23_T_TE.pdf", ["Fiat"]);

        Assert.Null(parsed.BuildYearFrom);
        Assert.Null(parsed.BuildYearTo);
    }

    [Theory]
    [InlineData("ShedaSoccorso_DE_01_01_12_T.pdf")]
    [InlineData("66_XXX_ShedaSoccorso_XXX_YY_ZZZ_DE_01_01.12_T.pdf")]
    public void Parse_CollectionFile_IsNamedAsCollection_WhateverTheLabel(string fileName)
    {
        Assert.True(StellantisRescueSheetLabelParser.IsCollection(fileName));
        Assert.Equal(
            StellantisRescueSheetLabelParser.CollectionModelName,
            StellantisRescueSheetLabelParser.Parse("Abarth Punto", fileName, ["Abarth"]).ModelName);
    }

    [Theory]
    [InlineData("70_406_FLAVIA_000.00.000_DE_01_05.12_T.pdf", "Flavia")]
    [InlineData("77_276_NUOVODOBLÒ_000_00_000_DE_01_06_22_T_TE.pdf", "Nuovodoblò")]
    [InlineData("lancia_ypsilon_2011_lpg.pdf", "Lancia Ypsilon 2011 Lpg")]
    public void LabelFromFileName(string fileName, string expected) =>
        Assert.Equal(expected, StellantisRescueSheetLabelParser.LabelFromFileName(fileName));

    [Fact]
    public void Parse_EmptyLabel_FallsBackToFilename()
    {
        var parsed = StellantisRescueSheetLabelParser.Parse("", "lancia_delta_lpg.pdf", ["Lancia"]);

        Assert.Equal("Delta", parsed.ModelName);
        Assert.Equal("LPG", parsed.FuelType);
    }
}
