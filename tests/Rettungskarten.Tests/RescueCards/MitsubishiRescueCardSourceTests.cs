using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Infrastructure.RescueCards.Parsing;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixture (mitsubishi_at_page.html): the real rescue-card section of mitsubishi-motors.at/services/rettungskarten
/// (16 cards, each an h2 heading plus a "Download PDF (x MB)" link), with one synthetic footer PDF that
/// isn't a rescue card.
/// </summary>
public sealed class MitsubishiRescueCardSourceTests
{
    private static MitsubishiRescueCardSource Source(StubHttpClientFactory factory) =>
        new(factory, NullLogger<MitsubishiRescueCardSource>.Instance);

    private static async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "mitsubishi_at_page.html"));
        return await Source(new StubHttpClientFactory().Html(MitsubishiRescueCardSource.PageUrl, html)).DiscoverAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DiscoverAsync_OneEntryPerCard_LabelledByTheCardHeading()
    {
        var entries = await DiscoverAsync();

        Assert.Equal(16, entries.Count); // the footer PDF outside /rettungskarten/ is not a card
        Assert.All(entries, e => Assert.Equal("DE", e.Parsed.LanguageCode));
        Assert.All(entries, e => Assert.NotNull(e.Parsed.ModelName));
        Assert.All(entries, e => Assert.StartsWith("https://www.mitsubishi-motors.at/content/dam/mitsubishi-motors-at/rettungskarten/", e.DownloadUrl));
        Assert.Equal(
            ["ASX", "Colt", "Eclipse Cross", "L200", "Outlander", "Space Star"],
            entries.Select(e => e.Parsed.ModelName!).Distinct().Order());
    }

    [Fact]
    public async Task DiscoverAsync_ParsesDrivetrainYearAndCab()
    {
        var entries = await DiscoverAsync();

        var asxPlugIn = Assert.Single(entries, e => e.Parsed.Variant == "ASX MY23 Plug-in Hybrid");
        Assert.Equal("ASX", asxPlugIn.Parsed.ModelName);
        Assert.Equal(2023, asxPlugIn.Parsed.BuildYearFrom);
        Assert.Null(asxPlugIn.Parsed.BuildYearTo);
        Assert.Equal("Plug-in Hybrid", asxPlugIn.Parsed.FuelType);

        // The drivetrain in front of the model year is not part of the model ("Outlander PHEV MY19").
        var outlanderPhev = Assert.Single(entries, e => e.Parsed.Variant == "Outlander PHEV MY19");
        Assert.Equal("Outlander", outlanderPhev.Parsed.ModelName);
        Assert.Equal("PHEV", outlanderPhev.Parsed.FuelType);

        var l200 = Assert.Single(entries, e => e.Parsed.Variant == "L200 MY20 Klubkabine");
        Assert.Equal("Klubkabine", l200.Parsed.BodyType);

        // A backtick in the CMS filename ("SpaceStar`20.pdf") is percent-encoded once.
        var spaceStar = Assert.Single(entries, e => e.Parsed.ModelName == "Space Star");
        Assert.Equal("https://www.mitsubishi-motors.at/content/dam/mitsubishi-motors-at/rettungskarten/SpaceStar%6020.pdf", spaceStar.DownloadUrl);
        Assert.Equal(2020, spaceStar.Parsed.BuildYearFrom);
    }

    [Fact]
    public async Task DownloadAsync_FetchesThePdf()
    {
        var entry = (await DiscoverAsync()).First(e => e.Parsed.ModelName == "Space Star");
        var factory = new StubHttpClientFactory().Bytes(entry.DownloadUrl!, StubHttpClientFactory.FakePdf());

        var result = await Source(factory).DownloadAsync(entry, CancellationToken.None);

        Assert.True(result.Success);
        Assert.All(factory.Requests, r => Assert.Equal(RettungskartenHttpClient.Name, r.ClientName));
    }

    [Theory]
    [InlineData("Rettungskarte COLT MY23 Hybrid", "Colt", 2023, "Hybrid", null)]
    [InlineData("Rettungskarte Eclipse Cross PHEV MY21", "Eclipse Cross", 2021, "PHEV", null)]
    [InlineData("Rettungskarte L200 MY20 Doppelkabine", "L200", 2020, null, "Doppelkabine")]
    [InlineData("Rettungskarte Space Star MY20", "Space Star", 2020, null, null)]
    [InlineData("Rettungskarte Pajero MY99", "Pajero", 1999, null, null)] // not 2099
    public void Parse_ReadsHeading(string heading, string model, int year, string? fuel, string? body)
    {
        var parsed = MitsubishiLabelParser.Parse(heading);

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(year, parsed.BuildYearFrom);
        Assert.Equal(fuel, parsed.FuelType);
        Assert.Equal(body, parsed.BodyType);
        Assert.Equal(ParseConfidence.High, parsed.ParseConfidence);
    }
}
