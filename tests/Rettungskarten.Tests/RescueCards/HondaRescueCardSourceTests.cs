using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Infrastructure.RescueCards.Parsing;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixture (honda_page.html) is a trimmed real slice of honda.de's rescue-sheet page: the Accord
/// section (CTA components directly in the column), the first three Civic sheets (each CTA wrapped
/// in an extra hmeContainer - the layout that broke a naive sibling walk on the live page), and the
/// page's unrelated CO2/consumption PDF link.
/// </summary>
public sealed class HondaRescueCardSourceTests
{
    private const string PageUrl = "https://www.honda.de/cars/services/download-rettungsdatenblaetter.html";

    private static async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "honda_page.html"));
        var factory = new StubHttpClientFactory().Html(PageUrl, html);
        return await new HondaRescueCardSource(factory, NullLogger<HondaRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DiscoverAsync_CollectsRescueSheetsOnly()
    {
        var entries = await DiscoverAsync();

        Assert.Equal(7, entries.Count); // 4 Accord + 3 Civic; the CO2 info PDF is not a rescue sheet
        Assert.All(entries, e => Assert.Contains("rettungsdatenblatt", e.RawFileNameOrLabel));
    }

    [Fact]
    public async Task DiscoverAsync_ReadsHeadingAndDetails_InBothLayouts()
    {
        var entries = await DiscoverAsync();

        var accord = Assert.Single(entries, e => e.Parsed.ChassisCode == "CU1/CU2/CU3");
        Assert.Equal("Accord", accord.Parsed.ModelName);
        Assert.Equal("Limousine", accord.Parsed.BodyType);
        Assert.Equal(2008, accord.Parsed.BuildYearFrom);
        Assert.Equal(2015, accord.Parsed.BuildYearTo);

        var civic = entries.Where(e => e.Parsed.ModelName == "Civic").ToList();
        Assert.Equal(3, civic.Count);
        Assert.All(civic, e => Assert.NotNull(e.Parsed.ChassisCode));
        Assert.All(civic, e => Assert.NotNull(e.Parsed.BuildYearFrom));

        var diesel = Assert.Single(civic, e => e.Parsed.FuelType == "Diesel");
        Assert.Equal(4, diesel.Parsed.Doors);
        Assert.Equal(2018, diesel.Parsed.BuildYearFrom);
        Assert.Null(diesel.Parsed.BuildYearTo);
    }

    [Theory]
    [InlineData("CR-V P:HEV | CR-V P:HEV | Amtlicher Typ: RS8 Bauzeitraum: ab 2023 PDF (1,79 MB)", "rettungsdatenblatt_cr-v_phev_suv_2023-11.pdf",
        "CR-V", "RS8", "Plug-in-Hybrid", "SUV", 2023)]
    [InlineData("Civic Type R | Civic Type R | Amtlicher Typ: 5dr Hatchback Bauzeitraum: ab 2022 PDF (3,02 MB)", "de_car_rettungsdatenblatt_civic_typer_2022.pdf",
        "Civic Type R", null, null, null, 2022)]
    [InlineData("Jazz | JAZZ | Amtlicher Typ: GG1/GG2/GG3/GG5/GG6/GE6 Bauzeitraum:2009 – 2015 PDF (228 KB)", "de_car_rettungsdatenblatt_jazz_2009.pdf",
        "Jazz", "GG1/GG2/GG3/GG5/GG6/GE6", null, null, 2009)]
    [InlineData("Honda e | Honda e | Amtlicher Typ: ZC7 Bauzeitraum: ab 2020 PDF (598 KB)", "rettungsdatenblatt_honda_e_2022-03.pdf",
        "Honda e", "ZC7", "Elektro", null, 2020)]
    public void Parser_HandlesRealLabels(string label, string file, string model, string? chassis, string? fuel, string? body, int from)
    {
        var parsed = HondaLabelParser.Parse(label, file);

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(chassis, parsed.ChassisCode);
        Assert.Equal(fuel, parsed.FuelType);
        Assert.Equal(body, parsed.BodyType);
        Assert.Equal(from, parsed.BuildYearFrom);
    }
}
