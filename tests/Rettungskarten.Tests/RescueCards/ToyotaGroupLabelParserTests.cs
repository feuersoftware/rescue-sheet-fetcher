using Rettungskarten.Infrastructure.RescueCards.ToyotaGroup;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>Labels below are real <c>data-gt-label</c> values from toyota.de / lexus.de (2026-09-29).</summary>
public class ToyotaGroupLabelParserTests
{
    [Theory]
    [InlineData("Prius II (HW2, 5-Türer, ab 2004-2009)", "Prius", "HW2", 2004, 2009)]
    [InlineData("Yaris (XPA1F, 5-Türer, ab 07/20)", "Yaris", "XPA1F", 2020, null)]
    [InlineData("Aygo X (ab Bj. 01/2022)", "Aygo X", null, 2022, null)]
    [InlineData("AYGO (AB1 3-Türer, ab 01/2005)", "Aygo", "AB1", 2005, null)]
    [InlineData("Corolla (E15EJ(a), Sedan 4-Türer, ab 6/2013)", "Corolla", "E15EJ", 2013, null)]
    [InlineData("RAV4 Hybrid XA5 (EU, M), ab 05/2020", "RAV4", "XA5", 2020, null)]
    [InlineData("Mirai (LHD1 11/2020)", "Mirai", null, 2020, null)]
    [InlineData("Land Cruiser (J12(EU) 5-Türer)", "Land Cruiser", "J12", null, null)]
    [InlineData("Yaris (XP9(a) Japan 3-Türer, ab 08/2005), (XP9F(a) Frankreich. 3-Türer, ab 10/2005)", "Yaris", "XP9", 2005, null)]
    public void ParseToyota_ModelChassisAndYears(string label, string model, string? chassis, int? from, int? to)
    {
        var parsed = ToyotaGroupLabelParser.ParseToyota(label, "x.pdf");

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(chassis, parsed.ChassisCode);
        Assert.Equal(from, parsed.BuildYearFrom);
        Assert.Equal(to, parsed.BuildYearTo);
        Assert.Equal("DE", parsed.LanguageCode);
    }

    [Theory]
    [InlineData("RAV4 Plug-in Hybrid, ab 06/2020", "RAV4PHV54_LHD_1_DE_tcm-17-2142372.pdf", "Plug-in Hybrid")]
    [InlineData("Camry (XV 7 (EU,M), ab 01/2019)", "RK_CAMRYHV_2019_tcm-17-172169.pdf", "Hybrid")]
    [InlineData("Toyota C-HR (ab 10/2023)", "C-HRHV20_DE.pdf", "Hybrid")]
    [InlineData("Mirai (AD1 (EU, M) 06/2015)", "Mirai_RK_tcm-17-467833.pdf", "Hydrogen")]
    [InlineData("Proace City EV (ab 2021)", "ProAce City EV_print.pdf", "Electric")]
    [InlineData("RAV4 (XA5 (EU,M), ab 11/2018)", "RK_RAV4_XA5_tcm-17-1578103.pdf", null)]
    public void ParseToyota_Drivetrain(string label, string fileName, string? fuel) =>
        Assert.Equal(fuel, ToyotaGroupLabelParser.ParseToyota(label, fileName).FuelType);

    [Theory]
    [InlineData("Lexus CT 200h - ab 2010", "CT", "Hybrid", 2010)]
    [InlineData("Lexus LC 500H – AB 01/2017", "LC", "Hybrid", 2017)]
    [InlineData("Lexus RX 450h+ - ab 02/2023", "RX", "Plug-in Hybrid", 2023)]
    [InlineData("Lexus RZ 450e - ab 02/2023", "RZ", "Electric", 2023)]
    [InlineData("Lexus NX 300h - 07/2014", "NX", "Hybrid", 2014)]
    [InlineData("Lexus LS 460 - ab 08/2006", "LS", null, 2006)]
    public void ParseLexus_SeriesDrivetrainAndYear(string label, string series, string? fuel, int from)
    {
        var parsed = ToyotaGroupLabelParser.ParseLexus(label);

        Assert.Equal(series, parsed.ModelName);
        Assert.Equal(fuel, parsed.FuelType);
        Assert.Equal(from, parsed.BuildYearFrom);
    }
}
