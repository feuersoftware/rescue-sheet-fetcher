using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Mercedes;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixtures are trimmed real pages from rk.mb-qr.com (2026-09-29): mercedes_overview.html keeps the
/// real class/body filter lists and seven real card tiles across all five portal brands, picked for
/// the label shapes that need care - a class name overlapping the brand prefix ("Mercedes-AMG GT"),
/// a class name equal to the brand's own name ("Mercedes-Maybach Maybach"), a two-model card
/// ("GL/GLS"), a smart tile, and an E-Klasse Coupé (own KBA series). mercedes_detail_page.html keeps
/// a detail page's PDF link plus the two general PDFs (towing guide, rescue guideline) on the same page.
/// </summary>
public sealed class MercedesRescueCardSourceTests : IDisposable
{
    private const string DetailUrl = "https://rk.mb-qr.com/de/177.085/";
    private const string PdfUrl = "https://rk.mb-qr.com/de/card/pdf/481/Mercedes-Benz_A-Klasse_250e_Sedan_2023_4d_Hybrid_DE_177.085v1.7.pdf/?837542";

    public MercedesRescueCardSourceTests() => Strings.OverrideCulture = new CultureInfo("en");

    public void Dispose() => Strings.OverrideCulture = null;

    private static StubHttpClientFactory Factory() =>
        new StubHttpClientFactory().Html(MercedesRescueCardSource.OverviewUrl, File.ReadAllText(Path.Combine("Fixtures", "mercedes_overview.html")));

    private static MercedesRescueCardSource Source(Brand brand, StubHttpClientFactory factory, DiscoveryResponseCache? cache = null) =>
        new(brand, factory, cache ?? new DiscoveryResponseCache(), NullLogger<MercedesRescueCardSource>.Instance);

    [Fact]
    public async Task DiscoverAsync_MercedesBenz_ReadsStructuredMetadataFromTheTile()
    {
        var entries = await Source(Brand.MercedesBenz, Factory()).DiscoverAsync(CancellationToken.None);

        Assert.Equal(3, entries.Count);
        var aClass = Assert.Single(entries, e => e.Parsed.ModelName == "A-Klasse");
        Assert.Equal(DetailUrl, aClass.DownloadUrl);
        Assert.Equal("177.085", aClass.RawFileNameOrLabel);
        Assert.Equal(MercedesRescueCardSource.OverviewUrl, aClass.SourcePageUrl);
        Assert.Equal("250e", aClass.Parsed.Variant);
        Assert.Equal("Limousine", aClass.Parsed.BodyType);
        Assert.Equal(2023, aClass.Parsed.BuildYearFrom);
        Assert.Null(aClass.Parsed.BuildYearTo);
        Assert.Equal("W177", aClass.Parsed.ChassisCode);
        Assert.Equal("Hybrid Benzin", aClass.Parsed.FuelType);
        Assert.Equal("DE", aClass.Parsed.LanguageCode);
        Assert.Equal(ParseConfidence.High, aClass.Parsed.ParseConfidence);
        Assert.Equal(ManufacturerGroup.MercedesBenzGroup, aClass.ManufacturerGroup);

        var gls = Assert.Single(entries, e => e.Parsed.ChassisCode == "166");
        Assert.Equal("GLS", gls.Parsed.ModelName);
        Assert.Equal("SUV", gls.Parsed.BodyType);
        Assert.Equal(2012, gls.Parsed.BuildYearFrom);
        Assert.Equal(2019, gls.Parsed.BuildYearTo);
    }

    [Fact]
    public async Task DiscoverAsync_ECoupe_IsNamedAfterItsOwnKbaSeries()
    {
        var entries = await Source(Brand.MercedesBenz, Factory()).DiscoverAsync(CancellationToken.None);

        var coupe = Assert.Single(entries, e => e.Parsed.ChassisCode == "C238");
        Assert.Equal("E-Klasse Coupé", coupe.Parsed.ModelName);
        Assert.Equal("Coupé", coupe.Parsed.BodyType);
        Assert.Null(coupe.Parsed.Variant);
    }

    [Fact]
    public async Task DiscoverAsync_ClassNameOverlappingBrandPrefix_IsStillSplitCorrectly()
    {
        var amg = Assert.Single(await Source(Brand.MercedesAmg, Factory()).DiscoverAsync(CancellationToken.None));
        Assert.Equal("AMG GT", amg.Parsed.ModelName);
        Assert.Equal("[GT / GT C / GT R / GT S]", amg.Parsed.Variant);
        Assert.Equal("C190", amg.Parsed.ChassisCode);
        Assert.Equal(2015, amg.Parsed.BuildYearFrom);
        Assert.Equal(2021, amg.Parsed.BuildYearTo);

        var maybach = Assert.Single(await Source(Brand.Maybach, Factory()).DiscoverAsync(CancellationToken.None));
        Assert.Equal("Maybach", maybach.Parsed.ModelName);
        Assert.Null(maybach.Parsed.Variant);
        Assert.Equal("Limousine", maybach.Parsed.BodyType);
    }

    [Fact]
    public async Task DiscoverAsync_SmartAndEq_AreFilteredByPortalBrand()
    {
        var smart = Assert.Single(await Source(Brand.Smart, Factory()).DiscoverAsync(CancellationToken.None));
        Assert.Equal("fortwo", smart.Parsed.ModelName);
        Assert.Equal("Coupé", smart.Parsed.BodyType);
        Assert.Equal("C453", smart.Parsed.ChassisCode);
        Assert.Equal("Elektrisch", smart.Parsed.FuelType);
        // Mercedes-built smart generations keep the brand's default group; only smart's own site overrides it.
        Assert.Null(smart.ManufacturerGroupOverride);

        var eq = Assert.Single(await Source(Brand.MercedesEq, Factory()).DiscoverAsync(CancellationToken.None));
        Assert.Equal("EQA", eq.Parsed.ModelName);
        Assert.Equal("250+/260", eq.Parsed.Variant);
    }

    [Fact]
    public async Task DiscoverAsync_AllFiveBrands_FetchTheOverviewOnlyOnce()
    {
        var factory = Factory();
        var cache = new DiscoveryResponseCache();
        Brand[] brands = [Brand.MercedesBenz, Brand.MercedesAmg, Brand.MercedesEq, Brand.Maybach, Brand.Smart];

        var counts = await Task.WhenAll(brands.Select(b => Source(b, factory, cache).DiscoverAsync(CancellationToken.None)));

        Assert.Equal(7, counts.Sum(c => c.Count));
        Assert.Single(factory.Requests);
    }

    [Fact]
    public async Task DownloadAsync_ResolvesThePdfLinkFromTheDetailPage()
    {
        var factory = Factory()
            .Html(DetailUrl, File.ReadAllText(Path.Combine("Fixtures", "mercedes_detail_page.html")))
            .Bytes(PdfUrl, StubHttpClientFactory.FakePdf());
        var source = Source(Brand.MercedesBenz, factory);
        var entry = Assert.Single(await source.DiscoverAsync(CancellationToken.None), e => e.RawFileNameOrLabel == "177.085");

        var result = await source.DownloadAsync(entry, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PdfUrl, factory.Requests.Last().Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task DownloadAsync_DetailPageWithoutPdfLink_IsAFailedResult()
    {
        var factory = Factory().Html(DetailUrl, "<html><body><p>Karte nicht verfügbar</p></body></html>");
        var source = Source(Brand.MercedesBenz, factory);
        var entry = Assert.Single(await source.DiscoverAsync(CancellationToken.None), e => e.RawFileNameOrLabel == "177.085");

        var result = await source.DownloadAsync(entry, CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task DiscoverAsync_PageWithoutCardList_Throws()
    {
        var factory = new StubHttpClientFactory().Html(MercedesRescueCardSource.OverviewUrl, "<html><body>Wartung</body></html>");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Source(Brand.MercedesBenz, factory).DiscoverAsync(CancellationToken.None));
    }

    [Fact]
    public void Constructor_BrandNotOnThePortal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Source(Brand.BMW, new StubHttpClientFactory()));
    }
}

public sealed class MercedesCardListParserTests
{
    [Fact]
    public void ParseLabel_UnknownClassAndBody_FallsBackToHeuristicWithoutThrowing()
    {
        var parsed = MercedesCardListParser.ParseLabel(
            "Mercedes-Benz Neuwagen 300 Van (W999) (ab 2027)", className: null, bodyName: null, typeNumber: null, fuelType: null);

        Assert.Equal("Neuwagen", parsed.ModelName);
        Assert.Equal(2027, parsed.BuildYearFrom);
        Assert.Equal("W999", parsed.ChassisCode);
        Assert.Equal(ParseConfidence.Heuristic, parsed.ParseConfidence);
    }

    [Fact]
    public void ParseLabel_LabelWithoutParentheticals_KeepsClassAndBody()
    {
        var parsed = MercedesCardListParser.ParseLabel(
            "Mercedes-Benz C-Klasse 200 T-Modell", "C-Klasse", "T-Modell", "S206", "Benzin");

        Assert.Equal("C-Klasse", parsed.ModelName);
        Assert.Equal("200", parsed.Variant);
        Assert.Equal("T-Modell", parsed.BodyType);
        Assert.Equal("S206", parsed.ChassisCode);
        Assert.Null(parsed.BuildYearFrom);
        Assert.Equal(ParseConfidence.Heuristic, parsed.ParseConfidence);
    }
}
