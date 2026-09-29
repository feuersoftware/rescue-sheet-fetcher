using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixtures are trimmed real pages: mitsubishi-motors.de's rescue-card page (just its PressMatrix
/// iframe), seven edition teasers of the embedded catalogue (incl. the odd titles: "Plug-in Hybrid
/// Outlander", "Electric Vehicle (i-MiEV)", "Modelljahr 2025x", "Lancer Evolution" without years) and
/// one edition page with its /d/ download button.
/// </summary>
public sealed class MitsubishiRescueCardSourceTests
{
    private const string PageUrl = "https://www.mitsubishi-motors.de/kundenservice/rettungskarten";
    private const string CatalogueUrl = "https://www.mitsubishi-publikationen.de/de/profiles/bf834dc9c730/editions/category/2756";
    private const string EditionUrl =
        "https://www.mitsubishi-publikationen.de/de/profiles/bf834dc9c730-mitsubishi-motors-prospekte/editions/asx-rettungsdatenblatt-ab-modelljahr-2023";

    private static string Fixture(string name) => File.ReadAllText(Path.Combine("Fixtures", name));

    private static MitsubishiRescueCardSource Source(StubHttpClientFactory factory) =>
        new(factory, NullLogger<MitsubishiRescueCardSource>.Instance);

    [Fact]
    public async Task DiscoverAsync_ReadsCatalogueFromIframe_OneEntryPerEdition()
    {
        var factory = new StubHttpClientFactory()
            .Html(PageUrl, Fixture("mitsubishi_rettungskarten_page.html"))
            .Html(CatalogueUrl, Fixture("mitsubishi_category.html"));

        var entries = await Source(factory).DiscoverAsync(CancellationToken.None);

        Assert.Equal(7, entries.Count);
        Assert.All(entries, e => Assert.Equal("DE", e.Parsed.LanguageCode));
        Assert.All(entries, e => Assert.StartsWith("https://www.mitsubishi-publikationen.de/de/profiles/", e.DownloadUrl));
        Assert.Equal(entries.Count, entries.Select(e => e.RawFileNameOrLabel).Distinct().Count());

        var asx = Assert.Single(entries, e => e.RawFileNameOrLabel == "asx-plug-in-hybrid-rettungsdatenblatt-ab-modelljahr-2023");
        Assert.Equal("ASX", asx.Parsed.ModelName);
        Assert.Equal("Plug-in Hybrid", asx.Parsed.FuelType);
        Assert.Equal(2023, asx.Parsed.BuildYearFrom);
        Assert.Null(asx.Parsed.BuildYearTo);
    }

    [Fact]
    public async Task DiscoverAsync_CatalogueDisallowedByRobotsTxt_ReportsNotSupported()
    {
        var factory = new StubHttpClientFactory()
            .Html(PageUrl, Fixture("mitsubishi_rettungskarten_page.html"))
            .Respond(CatalogueUrl, _ =>
            {
                // What RobotsTxtDelegatingHandler answers for a disallowed URL (the real site's
                // robots.txt disallows every path for every agent but Googlebot/Facebot).
                var blocked = new HttpResponseMessage((HttpStatusCode)451);
                blocked.Headers.Add(RobotsTxtDelegatingHandler.BlockedMarkerHeader, "robots.txt");
                return blocked;
            });

        await Assert.ThrowsAsync<NotSupportedException>(() => Source(factory).DiscoverAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DiscoverAsync_PageWithoutCatalogueIframe_Throws()
    {
        var factory = new StubHttpClientFactory().Html(PageUrl, "<html><body><p>moved</p></body></html>");

        await Assert.ThrowsAsync<InvalidDataException>(() => Source(factory).DiscoverAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_ResolvesTheEditionsShortLinkAtDownloadTime()
    {
        var factory = new StubHttpClientFactory()
            .Html(EditionUrl, Fixture("mitsubishi_edition.html"))
            .Bytes("https://www.mitsubishi-publikationen.de/d/6j47", StubHttpClientFactory.FakePdf());
        var entry = new RescueCardEntry(Brand.Mitsubishi, CatalogueUrl, EditionUrl, "asx-rettungsdatenblatt-ab-modelljahr-2023",
            MitsubishiRescueCardSource.ParseTitle("ASX Rettungsdatenblatt (ab Modelljahr 2023)"));

        var result = await Source(factory).DownloadAsync(entry, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("ASX Plug-in Hybrid Rettungsdatenblatt (ab Modelljahr 2023)", "ASX", "Plug-in Hybrid", null, null, 2023)]
    [InlineData("Outlander Rettungsdatenblatt (ab Modelljahr 2025x)", "Outlander", null, null, null, 2025)]
    [InlineData("Plug-in Hybrid Outlander Rettungsdatenblatt (ab Modelljahr 2014)", "Outlander", "Plug-in Hybrid", null, null, 2014)]
    [InlineData("L200 Doppelkabine Rettungsdatenblatt (ab Modelljahr 2020)", "L200", null, "Doppelkabine", null, 2020)]
    [InlineData("Colt 3-Türer Rettungsdatenblatt", "Colt", null, null, 3, null)]
    [InlineData("Lancer Sportback Rettungsdatenblatt (ab Modelljahr 2009)", "Lancer", null, "Sportback", null, 2009)]
    [InlineData("Electric Vehicle  (i-MiEV) Rettungsdatenblatt", "i-MiEV", "Elektro", null, null, null)]
    [InlineData("Grandis Hybrid Rettungsdatenblatt (ab Modelljahr 2026)", "Grandis", "Hybrid", null, null, 2026)]
    public void ParseTitle_SplitsVehicleAndYears(string title, string model, string? fuel, string? body, int? doors, int? from)
    {
        var parsed = MitsubishiRescueCardSource.ParseTitle(title);

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(fuel, parsed.FuelType);
        Assert.Equal(body, parsed.BodyType);
        Assert.Equal(doors, parsed.Doors);
        Assert.Equal(from, parsed.BuildYearFrom);
        Assert.Null(parsed.BuildYearTo);
    }
}
