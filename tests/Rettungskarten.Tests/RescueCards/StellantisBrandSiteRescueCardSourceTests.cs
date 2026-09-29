using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Stellantis;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixtures (stellantis_*_page.html) are the real rescue-sheet sections of each brand's German page,
/// cut out of the full page (2026-09) - the complete link lists, including the tricky parts: Jeep's
/// Wrangler sheet linked three times (once from an image), Alfa's 4C sheet linked twice, Abarth's
/// "PDF DOWNLOADEN" buttons labelled only by their box heading, the "Hier" collection link on fiat.de
/// and its placeholder-named twin on the Abarth Mopar page, lancia.de's http links and its two
/// differently-named sheets that share one label, and dodge.de's umlaut folder name.
/// </summary>
public sealed class StellantisBrandSiteRescueCardSourceTests : IDisposable
{
    public StellantisBrandSiteRescueCardSourceTests() => Strings.OverrideCulture = new CultureInfo("en");

    public void Dispose() => Strings.OverrideCulture = null;

    private static async Task<IReadOnlyList<RescueCardEntry>> Discover(Brand brand, params (string Url, string Fixture)[] pages)
    {
        var factory = new StubHttpClientFactory();
        foreach (var (url, fixture) in pages)
        {
            factory.Html(url, await File.ReadAllTextAsync(Path.Combine("Fixtures", fixture)));
        }

        var entries = await new StellantisBrandSiteRescueCardSource(
            brand, factory, NullLogger<StellantisBrandSiteRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        Assert.All(factory.Requests, r => Assert.Equal(RettungskartenHttpClient.BrowserName, r.ClientName));
        Assert.All(entries, e => Assert.Equal(brand, e.Brand));
        Assert.All(entries, e => Assert.Equal("DE", e.Parsed.LanguageCode));
        Assert.Equal(entries.Count, entries.Select(e => e.RawFileNameOrLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        return entries;
    }

    [Fact]
    public async Task Fiat_ParsesLabels_AndKeepsTheCollectionAsOneCombinedEntry()
    {
        var entries = await Discover(Brand.Fiat, ("https://www.fiat.de/besitzer/rettungsdatenblaetter", "stellantis_fiat_page.html"));

        Assert.Equal(9, entries.Count);

        var collection = Assert.Single(entries, e => e.Scope == DocumentScope.Combined);
        Assert.Equal(StellantisRescueSheetLabelParser.CollectionModelName, collection.Parsed.ModelName);
        Assert.Equal("https://www.fiat.de/content/dam/fiat2023/de/aftersales-rettungsdatenblaetter/ShedaSoccorso_DE_01_01_12_T.pdf", collection.DownloadUrl);

        var electric = Assert.Single(entries, e => e.RawFileNameOrLabel == "500e-Rettungsdatenblatt.pdf");
        Assert.Equal("500", electric.Parsed.ModelName);
        Assert.Equal("Elektro", electric.Parsed.FuelType);

        var tipo = Assert.Single(entries, e => e.RawFileNameOrLabel == "Fiat_Tipo_5Tuerer.pdf");
        Assert.Equal("Tipo", tipo.Parsed.ModelName);
        Assert.Equal(5, tipo.Parsed.Doors);

        Assert.Equal("Kombi", Assert.Single(entries, e => e.RawFileNameOrLabel == "Fiat_Tipo_5Tuerer_Kombi.pdf").Parsed.BodyType);
        Assert.Equal("600", Assert.Single(entries, e => e.RawFileNameOrLabel == "365_FIAT_DE_01_06_23_T_TE.pdf").Parsed.ModelName);
    }

    [Fact]
    public async Task FiatProfessional_ReadsElectricPrefixAsFuelType()
    {
        var entries = await Discover(
            Brand.FiatProfessional,
            ("https://www.fiat.de/professional/besitzer/rettungsdatenblaetter", "stellantis_fiatpro_page.html"));

        Assert.Equal(["Doblo", "Ducato", "Scudo", StellantisRescueSheetLabelParser.CollectionModelName],
            entries.Select(e => e.Parsed.ModelName).Order(StringComparer.Ordinal));
        Assert.All(entries.Where(e => e.Scope == DocumentScope.Single), e => Assert.Equal("Elektro", e.Parsed.FuelType));

        // "NUOVODOBLÒ" - a non-ASCII filename, escaped in the URL, decoded in the raw name.
        var doblo = Assert.Single(entries, e => e.Parsed.ModelName == "Doblo");
        Assert.Contains("%C3%92", doblo.DownloadUrl);
    }

    [Fact]
    public async Task Jeep_DeduplicatesRepeatedLinks_AndReadsWranglerPlatform()
    {
        var entries = await Discover(Brand.Jeep, ("https://www.jeep.de/mopar/rettungsdatenblaetter", "stellantis_jeep_page.html"));

        Assert.Equal(14, entries.Count); // 17 links, 3 of them repeats

        var wrangler4Door = Assert.Single(entries, e => e.RawFileNameOrLabel == "57_685_WRANGLERJL_000_00_000_DE_01_06_18_T_XX.pdf");
        Assert.Equal("Wrangler", wrangler4Door.Parsed.ModelName);
        Assert.Equal(4, wrangler4Door.Parsed.Doors);
        Assert.Equal("JL", wrangler4Door.Parsed.ChassisCode);

        var grandCherokee = Assert.Single(entries, e => e.Parsed.ModelName == "Grand Cherokee");
        Assert.Equal("Plug-in-Hybrid", grandCherokee.Parsed.FuelType);

        Assert.Equal(3, entries.Count(e => e.Parsed.ModelName == "Avenger"));
        Assert.All(entries, e => Assert.Equal(DocumentScope.Single, e.Scope));
    }

    [Fact]
    public async Task AlfaRomeo_MergesStandardFilenameData_AndSplitsGluedSportwagon()
    {
        var entries = await Discover(Brand.AlfaRomeo, ("https://www.alfaromeo.de/rettungsdaten-blaetter", "stellantis_alfaromeo_page.html"));

        Assert.Equal(17, entries.Count); // 18 links, the 4C sheet twice ("4C", "Spider")

        var junior = Assert.Single(entries, e => e.RawFileNameOrLabel == "Alfa_Romeo_JUNIOR_BEV__SUV_2024_5d_Electric_de.pdf");
        Assert.Equal("Junior", junior.Parsed.ModelName);
        Assert.Equal("SUV", junior.Parsed.BodyType);
        Assert.Equal(2024, junior.Parsed.BuildYearFrom);
        Assert.Null(junior.Parsed.BuildYearTo);
        Assert.Equal(5, junior.Parsed.Doors);
        Assert.Equal("Elektro", junior.Parsed.FuelType);

        // The mild hybrid's filename says fuel "FC"; the label's "Ibrida" wins.
        Assert.Equal("Hybrid", Assert.Single(entries, e => e.RawFileNameOrLabel.Contains("JUNIOR_MHEV")).Parsed.FuelType);

        var sportwagon = Assert.Single(entries, e => e.RawFileNameOrLabel == "AlfaRomeo_159SW.pdf");
        Assert.Equal("159", sportwagon.Parsed.ModelName);
        Assert.Equal("Kombi", sportwagon.Parsed.BodyType);

        Assert.Equal(2, entries.Count(e => e.Parsed.ModelName == "Giulia")); // incl. Quadrifoglio
        Assert.Equal("Plug-in-Hybrid", Assert.Single(entries, e => e.RawFileNameOrLabel == "AlfaRomeo_Tonale-Plug-In-Hybrid.pdf").Parsed.FuelType);
    }

    [Fact]
    public async Task Lancia_ParsesFilenames_AndUpgradesHttpLinks()
    {
        var entries = await Discover(Brand.Lancia, ("https://www.lancia.de/mopar/rettungsdatenblaetter", "stellantis_lancia_page.html"));

        Assert.Equal(14, entries.Count);
        Assert.All(entries, e => Assert.StartsWith("https://www.lancia.de/", e.DownloadUrl));

        // Both files are labelled "Lancia Ypsilon 2011 LPG" on the page; only one of them is the 2011 model.
        var ypsilonLpg = Assert.Single(entries, e => e.RawFileNameOrLabel == "lancia_ypsilon_lpg.pdf");
        Assert.Equal("Ypsilon", ypsilonLpg.Parsed.ModelName);
        Assert.Null(ypsilonLpg.Parsed.BuildYearFrom);
        Assert.Equal("LPG", ypsilonLpg.Parsed.FuelType);

        var ypsilon2011 = Assert.Single(entries, e => e.RawFileNameOrLabel == "lancia_ypsilon_2011_natural_power.pdf");
        Assert.Equal(2011, ypsilon2011.Parsed.BuildYearFrom);
        Assert.Equal("Erdgas", ypsilon2011.Parsed.FuelType);

        // "hier downloaden" link to a Stellantis document-code filename.
        Assert.Equal("Flavia", Assert.Single(entries, e => e.RawFileNameOrLabel.StartsWith("70_406_FLAVIA")).Parsed.ModelName);
    }

    [Fact]
    public async Task Abarth_ReadsButtonHeadings_AcrossBothPages()
    {
        var entries = await Discover(
            Brand.Abarth,
            ("https://www.abarth.de/rettungsdatenblaetter", "stellantis_abarth_page.html"),
            ("https://www.abarth.de/mopar/rettungsdatenblaetter", "stellantis_abarth_mopar_page.html"));

        // 595, 695 Tributo Ferrari, Grande Punto (brand page; "124 Spider" has no link), 500e and the
        // collection linked four times (Mopar page).
        Assert.Equal(["500e", "595", "695", "Grande Punto", StellantisRescueSheetLabelParser.CollectionModelName],
            entries.Select(e => e.Parsed.ModelName).Order(StringComparer.Ordinal));
        Assert.Equal("Abarth 695 Tributo Ferrari", Assert.Single(entries, e => e.Parsed.ModelName == "695").Parsed.Variant);
        Assert.Equal(DocumentScope.Combined, Assert.Single(entries, e => e.RawFileNameOrLabel.Contains("ShedaSoccorso")).Scope);
    }

    [Fact]
    public async Task Dodge_ParsesModelYearRanges_OverHttp()
    {
        var entries = await Discover(Brand.Dodge, ("http://www.dodge.de/informationen-fur-rettungskrafte.html", "stellantis_dodge_page.html"));

        Assert.Equal(6, entries.Count);
        Assert.All(entries, e => Assert.StartsWith("http://www.dodge.de/rettungsdatenbl%C3%A4tter/", e.DownloadUrl));

        var caliberOld = Assert.Single(entries, e => e.RawFileNameOrLabel == "Dodge_Caliber_MY2006-2011.pdf");
        Assert.Equal(("Caliber", 2006, 2011), (caliberOld.Parsed.ModelName, caliberOld.Parsed.BuildYearFrom, caliberOld.Parsed.BuildYearTo));

        var caliberNew = Assert.Single(entries, e => e.RawFileNameOrLabel == "Dodge_Caliber_ab_MY2012.pdf");
        Assert.Equal((2012, (int?)null), (caliberNew.Parsed.BuildYearFrom, caliberNew.Parsed.BuildYearTo));

        Assert.Equal("Journey", Assert.Single(entries, e => e.RawFileNameOrLabel == "Dodge_JourneyECO_ab_MY2009.pdf").Parsed.ModelName);
    }

    [Fact]
    public async Task DiscoverAsync_PageFailure_Throws()
    {
        var factory = new StubHttpClientFactory().Status("https://www.jeep.de/mopar/rettungsdatenblaetter", System.Net.HttpStatusCode.NotFound);
        var source = new StellantisBrandSiteRescueCardSource(Brand.Jeep, factory, NullLogger<StellantisBrandSiteRescueCardSource>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.DiscoverAsync(CancellationToken.None));
    }

    [Fact]
    public void Constructor_UnsupportedBrand_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StellantisBrandSiteRescueCardSource(Brand.Peugeot, new StubHttpClientFactory(), NullLogger<StellantisBrandSiteRescueCardSource>.Instance));
}
