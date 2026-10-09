using Rettungskarten.Infrastructure.RescueCards.RenaultGroup;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>Labels and file names below are real ones from renault.de / dacia.de (2026-09-29).</summary>
public class RenaultGroupLabelParserTests
{
    private static readonly string[] Renault = ["Renault"];
    private static readonly string[] Dacia = ["Dacia"];

    [Theory]
    [InlineData("CAPTUR 2 - 2021", "Renault_Captur2__Hatchback_2021_5d_LPG_DE.pdf", "Captur", 2021, "LPG")]
    [InlineData("TRAFIC E-TECH ELEKTRISCH", "Trafic Van E-Tech Electric Van 2023 3d Electric DE.pdf", "Trafic", 2023, "Electric")]
    [InlineData("RENAULT 4 E-TECH ELEKTRISCH", "Renault-Renault-4-E-Tech-Electric-SUV-2025-5d-Electric-DE.pdf", "Renault 4", 2025, "Electric")]
    [InlineData("KANGOO E-TECH", "Renault_Kangoo_E-Tech Electric_MPV_2022_5d_Electric_DE_ueberarbeitet_fuer_Veroeffentlichung.pdf", "Kangoo", 2022, "Electric")]
    [InlineData("CAPTUR 2 - 2019", "Renault_Captur2__Hatchback_2019_5d_GD_DE.pdf", "Captur", 2019, "Petrol/Diesel")]
    public void Parse_StandardAndDamagedStandardFileNames(string label, string fileName, string model, int yearFrom, string fuel)
    {
        var parsed = RenaultGroupLabelParser.Parse(label, fileName, Renault);

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(yearFrom, parsed.BuildYearFrom);
        Assert.Null(parsed.BuildYearTo);
        Assert.Equal(fuel, parsed.FuelType);
        Assert.NotNull(parsed.Doors);
    }

    [Theory]
    [InlineData("CAPTUR E-TECH 2 PLUG-IN-HYBRID", "Rettungskarte_Captur II PHEV_de.pdf", "Plug-in Hybrid")]
    [InlineData("SCENIC 4 HYBRID ASSIST", "renault_rettungsdatenblatt_scenic_5_2018.pdf", "Mild Hybrid")]
    [InlineData("FLUENCE Z.E.", "renault_rettungsdatenblatt_fluence_ze_2014.pdf", "Electric")]
    [InlineData("ZOE E-TECH 1", "renault_rettungsdatenblatt_zoe_ze_2014.pdf", "Electric")]
    [InlineData("CLIO FULL HYBRID E-TECH", "fad Clio V HEV de.pdf", "Hybrid")]
    [InlineData("TWINGO 1", "renault_rettungsdatenblatt_twingo_1_2014.pdf", null)]
    public void Parse_FuelFromLabelThenFileNameKeywords(string label, string fileName, string? fuel) =>
        Assert.Equal(fuel, RenaultGroupLabelParser.Parse(label, fileName, Renault).FuelType);

    [Theory]
    [InlineData("renault_rettungsdatenblatt_megane_3_2014.pdf")]
    [InlineData("renault_rettungsdatenblatt_koleos_ph_2_03_2016_DE.pdf")]
    [InlineData("renault_rettungsdatenblatt_alaskan_11_2017__17_04_2018_DE.pdf")]
    public void Parse_OldFileNameDatesAreNotBuildYears(string fileName)
    {
        var parsed = RenaultGroupLabelParser.Parse("MODEL 3", fileName, Renault);

        Assert.Null(parsed.BuildYearFrom);
        Assert.Null(parsed.BuildYearTo);
    }

    [Theory]
    [InlineData("Logan MCV 1 | Rettungsdatenblatt Logan 2007 bis 2013", "RDB-Logan-MCV-2007-2013.pdf", "Logan", 2007, 2013)]
    [InlineData("Dokker Express | Rettungsdatenblatt Dokker Express ab 2012", "RDB-Dokker-Express-2012-2.pdf", "Dokker", 2012, null)]
    [InlineData("Sandero 3 LPG (ab 2021)", "Rettungskarte-Dacia-Sandero-3-LPG-DE-2021.pdf", "Sandero", 2021, null)]
    [InlineData("Spring (ab 2024) | Rettungsdatenblatt Spring (ab 2024)", "Dacia_Spring_Hatchback_2024_5d_Electric_DE.pdf", "Spring", 2024, null)]
    public void Parse_DaciaLabelAndTitle(string label, string fileName, string model, int yearFrom, int? yearTo)
    {
        var parsed = RenaultGroupLabelParser.Parse(label, fileName, Dacia);

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(yearFrom, parsed.BuildYearFrom);
        Assert.Equal(yearTo, parsed.BuildYearTo);
        Assert.Equal("DE", parsed.LanguageCode);
    }

    [Theory]
    [InlineData("Renault 19", "Renault 19")]
    [InlineData("VEL SATIS", "Vel Satis")]
    [InlineData("GRAND SCENIC", "Grand Scenic")]
    [InlineData("MEGANE 1 & MEGANE SCENIC", "Megane")]
    [InlineData("KOLEOS PH2", "Koleos")]
    public void ExtractModelName_KeepsMarketingNamesAndDropsGenerations(string label, string expected) =>
        Assert.Equal(expected, RenaultGroupLabelParser.ExtractModelName(label, Renault));
}
