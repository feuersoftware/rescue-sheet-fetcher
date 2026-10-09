using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixtures are trimmed real pages of auto.suzuki.de: the overview's download + category blocks, the
/// "Modelle bis 2020" category (model tiles only in the Next.js RSC payload) and two of its model pages
/// (Swift: nine sheets incl. empty duplicate anchors and a non-breaking-space label; Vitara: one sheet
/// whose label has no year).
/// </summary>
public sealed class SuzukiRescueCardSourceTests
{
    private const string Category2020 = "https://auto.suzuki.de/service/dokumente-hilfe/modelle-bis-2020";

    private static string Fixture(string name) => File.ReadAllText(Path.Combine("Fixtures", name));

    [Fact]
    public async Task DiscoverAsync_WalksCategoriesAndModelPages_SkippingFailingPages()
    {
        // The other three categories fail (404) and all but two model pages are unknown to the stub -
        // both must only be logged, not fail discovery.
        var factory = new StubHttpClientFactory()
            .Html(SuzukiRescueCardSource.OverviewUrl, Fixture("suzuki_overview.html"))
            .Html(Category2020, Fixture("suzuki_category.html"))
            .Status("https://auto.suzuki.de/service/dokumente-hilfe/aktuelle-modelle", HttpStatusCode.NotFound)
            .Status("https://auto.suzuki.de/service/dokumente-hilfe/modelle-bis-2025", HttpStatusCode.NotFound)
            .Status("https://auto.suzuki.de/service/dokumente-hilfe/modelle-bis-2024", HttpStatusCode.NotFound)
            .Html(Category2020 + "/swift-dokumente-bis-2020", Fixture("suzuki_model_swift.html"))
            .Html(Category2020 + "/vitara-dokumente-bis-2020", Fixture("suzuki_model_vitara.html"));

        var entries = await new SuzukiRescueCardSource(factory, NullLogger<SuzukiRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        Assert.Equal(9 + 1, entries.Count);
        Assert.Equal(9, entries.Count(e => e.Parsed.ModelName == "Swift"));
        Assert.Equal(entries.Count, entries.Select(e => e.RawFileNameOrLabel).Distinct().Count());
        // The overview's general rescue guide ("Allgemeine Informationen") is not a sheet.
        Assert.DoesNotContain(entries, e => e.RawFileNameOrLabel.StartsWith("Rettungsleit"));

        var vitara = Assert.Single(entries, e => e.Parsed.ModelName == "Vitara");
        Assert.Null(vitara.Parsed.BuildYearFrom);
        Assert.Equal(2020, vitara.Parsed.BuildYearTo); // from the "Modelle bis 2020" category

        // Its only anchor with text is the second of two linking the same PDF.
        var swift2010 = Assert.Single(entries, e => e.RawFileNameOrLabel == "Rettungskarte-Suzuki-Swift-NZ-3T_2010.pdf");
        Assert.Equal(2010, swift2010.Parsed.BuildYearFrom);
        Assert.Equal(3, swift2010.Parsed.Doors);
    }

    [Fact]
    public async Task FindModelPagesAsync_ReadsTilesFromRscPayload()
    {
        var context = AngleSharp.BrowsingContext.New(AngleSharp.Configuration.Default);

        var pages = await SuzukiRescueCardSource.FindModelPagesAsync(context, Fixture("suzuki_category.html"), Category2020, CancellationToken.None);

        Assert.Equal(13, pages.Count);
        Assert.Contains(pages, p => p.ModelName == "SX4 S-Cross" && p.PageUrl == Category2020 + "/s4x-s-cross-dokumente-bis-2020");
    }

    [Theory]
    [InlineData("Rettungskarte Swift 3-Türer MY ab 2005  ↓", null, 2005, null, 3, null)]
    [InlineData("Rettungskarte Swift Hybrid bis 2020 ↓", null, null, 2020, null, "Hybrid")]
    [InlineData("Rettungskarte Swift 5-Türer MY 2010 ↓", 2024, 2010, null, 5, null)]
    [InlineData("Rettungskarte S-Cross 1.5 DUALJET ↓", 2024, null, 2024, null, null)]
    [InlineData("Rettungskarte Across  ↓", null, null, null, null, null)]
    public void ParseLabel_TakesYearsFromLabelThenCategory(string label, int? categoryEndYear, int? from, int? to, int? doors, string? fuel)
    {
        var parsed = SuzukiRescueCardSource.ParseLabel("Model", label, categoryEndYear);

        Assert.Equal("Model", parsed.ModelName);
        Assert.Equal(from, parsed.BuildYearFrom);
        Assert.Equal(to, parsed.BuildYearTo);
        Assert.Equal(doors, parsed.Doors);
        Assert.Equal(fuel, parsed.FuelType);
        Assert.DoesNotContain("Rettungskarte", parsed.Variant);
        Assert.DoesNotContain("↓", parsed.Variant);
    }

    [Theory]
    [InlineData("https://auto.suzuki.de/service/dokumente-hilfe/modelle-bis-2024", 2024)]
    [InlineData("https://auto.suzuki.de/service/dokumente-hilfe/aktuelle-modelle", null)]
    public void ParseCategoryEndYear_ReadsTheSlug(string url, int? expected) =>
        Assert.Equal(expected, SuzukiRescueCardSource.ParseCategoryEndYear(url));
}
