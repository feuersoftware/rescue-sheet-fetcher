using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// DiscoverAsync-level tests for the single-page sources (BYD, Isuzu, MAXUS, RUF, StreetScooter) against
/// trimmed real pages, plus Polestar's placeholder. Every fixture keeps at least one real non-sheet
/// document of the same page (BYD manuals/quick guides, Isuzu's registration-document explainer) to
/// lock in its exclusion. Nothing here asserts on localized text, so Strings.OverrideCulture is left
/// alone (setting/resetting that process-global value would race with the culture-specific tests
/// running in parallel).
/// </summary>
public sealed class SmallBrandSourcesTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine("Fixtures", name));

    [Fact]
    public async Task Byd_TakesOnlyRescueSheetItems()
    {
        var factory = new StubHttpClientFactory().Html(BydRescueCardSource.PageUrl, Fixture("byd_downloads.html"));

        var entries = await new BydRescueCardSource(factory, NullLogger<BydRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        Assert.Equal(["ATTO 2", "ATTO 3", "DOLPHIN", "SEAL 6", "SEAL U"], entries.Select(e => e.Parsed.ModelName).Order());
        var seal6 = Assert.Single(entries, e => e.Parsed.ModelName == "SEAL 6");
        Assert.Equal("DM-i", seal6.Parsed.FuelType);
        Assert.Equal("Touring", seal6.Parsed.BodyType);
        Assert.StartsWith("https://cdn.prod.website-files.com/", seal6.DownloadUrl);
        Assert.Equal("Elektro", Assert.Single(entries, e => e.Parsed.ModelName == "DOLPHIN").Parsed.FuelType);
    }

    [Theory]
    [InlineData("Rettungskarte ATTO 3 EVO", "ATTO 3", "Elektro")]
    [InlineData("Rettungskarte SEAL U DM-i", "SEAL U", "DM-i")]
    [InlineData("Rettungskarte ATTO 2 Comfort", "ATTO 2", "Elektro")]
    [InlineData("Rettungskarte DOLPHIN SURF", "DOLPHIN SURF", "Elektro")]
    public void Byd_ParseTitle(string title, string model, string fuel)
    {
        var parsed = BydRescueCardSource.ParseTitle(title);

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(fuel, parsed.FuelType);
        Assert.Equal(title["Rettungskarte ".Length..], parsed.Variant);
    }

    [Fact]
    public async Task Isuzu_ReadsTypeCodeFromHeading_AndSkipsNonSheets()
    {
        var factory = new StubHttpClientFactory().Html(IsuzuRescueCardSource.PageUrl, Fixture("isuzu_page.html"));

        var entries = await new IsuzuRescueCardSource(factory, NullLogger<IsuzuRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        Assert.Equal(5, entries.Count); // not the "Zulassungsbescheinigung Teil 1" explainer
        var current = Assert.Single(entries, e => e.Parsed.ChassisCode == "BTF");
        Assert.Equal("D-MAX", current.Parsed.ModelName);
        Assert.Equal(2020, current.Parsed.BuildYearFrom);
        Assert.Null(current.Parsed.BuildYearTo);

        var nSeries = entries.Where(e => e.Parsed.ModelName == "N-Serie").ToList();
        Assert.Equal(2, nSeries.Count);
        Assert.All(nSeries, e => Assert.Equal("N85, N75", e.Parsed.ChassisCode));
        Assert.Contains(nSeries, e => e.Parsed.BodyType == "Einzelkabine");
        Assert.All(nSeries, e => Assert.Equal(2010, e.Parsed.BuildYearTo));
    }

    [Fact]
    public async Task Maxus_OneEntryPerModelTile()
    {
        var factory = new StubHttpClientFactory().Html(MaxusRescueCardSource.PageUrl, Fixture("maxus_page.html"));

        var entries = await new MaxusRescueCardSource(factory, NullLogger<MaxusRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        Assert.Equal(7, entries.Count);
        Assert.Contains(entries, e => e.Parsed.ModelName == "EV 80" && e.Parsed.FuelType == "Elektro");
        Assert.Contains(entries, e => e.Parsed.ModelName == "eDELIVER 3"); // the "sicherheitsdatenblatt_..." file
        Assert.Null(Assert.Single(entries, e => e.Parsed.ModelName == "DELIVER 7").Parsed.FuelType);
        Assert.Equal("Pick-up", Assert.Single(entries, e => e.Parsed.ModelName == "eTERRON 9").Parsed.BodyType);
    }

    [Fact]
    public async Task Ruf_ThreeElectricCars()
    {
        var factory = new StubHttpClientFactory().Html(RufRescueCardSource.PageUrl, Fixture("ruf_page.html"));

        var entries = await new RufRescueCardSource(factory, NullLogger<RufRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        Assert.Equal(3, entries.Count);
        Assert.Equal(2, entries.Count(e => e.Parsed.ModelName == "eRUF"));
        Assert.Contains(entries, e => e.Parsed.ModelName == "eRUF Stromster");
        Assert.All(entries, e => Assert.Equal("Elektro", e.Parsed.FuelType));
    }

    [Fact]
    public async Task Streetscooter_LabelFromSiblingTitle_KeepsDecomposedUmlautInUrl()
    {
        var factory = new StubHttpClientFactory().Html(StreetscooterRescueCardSource.PageUrl, Fixture("streetscooter_page.html"));

        var entries = await new StreetscooterRescueCardSource(factory, NullLogger<StreetscooterRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        Assert.Equal(2, entries.Count);
        var work = Assert.Single(entries, e => e.Parsed.ModelName == "WORK");
        Assert.Equal("WORK & WORK L", work.Parsed.Variant);
        var xl = Assert.Single(entries, e => e.Parsed.ModelName == "WORK XL");
        // The server only answers the NFD spelling ("a" + combining diaeresis) the page uses.
        Assert.Contains("a%CC%88tter", xl.DownloadUrl);
    }

    [Fact]
    public async Task Polestar_ReportsNotSupported_WithoutAnyRequest()
    {
        var factory = new StubHttpClientFactory();

        await Assert.ThrowsAsync<NotSupportedException>(() => new PolestarRescueCardSource(factory).DiscoverAsync(CancellationToken.None));
        Assert.Empty(factory.Requests);
    }
}
