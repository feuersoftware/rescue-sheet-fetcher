using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.ToyotaGroup;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixtures are trimmed slices of the real toyota.de / lexus.de rescue-sheet pages: real anchors with
/// their <c>data-gt-label</c>, every PDF linked twice as on the live pages, including the general
/// "Rettungsleitfaden" guides (German and English) that must be excluded and Scene7 URLs containing a
/// literal "ä".
/// </summary>
public sealed class ToyotaGroupRescueCardSourceTests
{
    private const string ToyotaPageUrl = "https://www.toyota.de/zubehoer-service/fahrzeuginformationen/rettungsdatenblaetter";
    private const string LexusPageUrl = "https://www.lexus.de/lexus-besitzer/fahrzeuginformationen/rettungsdatenblaetter";

    private static async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(Brand brand, string pageUrl, string fixture)
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", fixture));
        var factory = new StubHttpClientFactory().Html(pageUrl, html);
        var source = new ToyotaGroupRescueCardSource(brand, factory, NullLogger<ToyotaGroupRescueCardSource>.Instance);
        return await source.DiscoverAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DiscoverAsync_Toyota_DeduplicatesAndExcludesGeneralGuides()
    {
        var entries = await DiscoverAsync(Brand.Toyota, ToyotaPageUrl, "toyota_page.html");

        Assert.Equal(10, entries.Count);
        Assert.DoesNotContain(entries, e => e.RawFileNameOrLabel.Contains("Rettungsleitfaden"));
        Assert.All(entries, e => Assert.Equal("DE", e.Parsed.LanguageCode));
        // The umlaut in Scene7's folder name is percent-encoded exactly once.
        Assert.All(entries, e => Assert.Contains("rettungsdatenbl%C3%A4tter", e.DownloadUrl));
    }

    [Fact]
    public async Task DiscoverAsync_Toyota_ParsesLabelMetadata()
    {
        var entries = await DiscoverAsync(Brand.Toyota, ToyotaPageUrl, "toyota_page.html");

        var auris = Assert.Single(entries, e => e.RawFileNameOrLabel == "Auris-HV_RLF_tcm-17-172152.pdf");
        Assert.Equal("Auris", auris.Parsed.ModelName);
        Assert.Equal("E15UT", auris.Parsed.ChassisCode);
        Assert.Equal(5, auris.Parsed.Doors);
        Assert.Equal(2010, auris.Parsed.BuildYearFrom);
        Assert.Equal("Hybrid", auris.Parsed.FuelType);

        var priusPlus = Assert.Single(entries, e => e.Parsed.ModelName == "Prius Plus");
        Assert.Equal("XW3", priusPlus.Parsed.ChassisCode);

        var hilux = Assert.Single(entries, e => e.Parsed.ModelName == "Hilux");
        Assert.Equal("Single Cab", hilux.Parsed.BodyType);
        Assert.Equal("AN1P", hilux.Parsed.ChassisCode);

        // Two-digit year "ab 09/19".
        var chr = Assert.Single(entries, e => e.Parsed.ModelName == "C-HR");
        Assert.Equal(2019, chr.Parsed.BuildYearFrom);
        Assert.Equal("MAXH10", chr.Parsed.ChassisCode);

        // Plug-in only visible in the file name ("RK_PRIUSPHV_XW52").
        var priusPhv = Assert.Single(entries, e => e.Parsed.ChassisCode == "XW5P");
        Assert.Equal("Plug-in Hybrid", priusPhv.Parsed.FuelType);

        // A market marker is not a chassis code; the sheet for two drivetrains gets no fuel type.
        Assert.Null(Assert.Single(entries, e => e.Parsed.ModelName == "bZ4X").Parsed.ChassisCode);
        Assert.Null(Assert.Single(entries, e => e.Parsed.ModelName == "Proace Verso").Parsed.FuelType);
    }

    [Fact]
    public async Task DiscoverAsync_Lexus_UsesSeriesAsModelName()
    {
        var entries = await DiscoverAsync(Brand.Lexus, LexusPageUrl, "lexus_page.html");

        Assert.Equal(6, entries.Count); // rescue guide excluded
        Assert.All(entries, e => Assert.Equal(Brand.Lexus, e.Brand));

        var nx = Assert.Single(entries, e => e.Parsed.Variant == "NX 450h+");
        Assert.Equal("NX", nx.Parsed.ModelName);
        Assert.Equal("Plug-in Hybrid", nx.Parsed.FuelType);
        Assert.Equal(2021, nx.Parsed.BuildYearFrom);

        var lc = Assert.Single(entries, e => e.Parsed.ModelName == "LC");
        Assert.Equal("Cabriolet", lc.Parsed.BodyType);
        Assert.Equal("Electric", Assert.Single(entries, e => e.Parsed.Variant == "UX 300e").Parsed.FuelType);
    }
}
