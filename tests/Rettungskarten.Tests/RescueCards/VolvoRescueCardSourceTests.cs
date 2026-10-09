using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Infrastructure.RescueCards.Parsing;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>Fixture (volvo_page.html): six real links from volvocars.com's rescue-guide page, covering
/// the classic "Typ" labels, a label with a stray underscore, the underscore-separated year range and
/// a newer standard-convention filename used as link text.</summary>
public sealed class VolvoRescueCardSourceTests
{
    private const string PageUrl = "https://www.volvocars.com/de/l/zubehoer-und-services/rettungsleitfaeden/";

    [Fact]
    public async Task DiscoverAsync_ParsesAllLabelShapes_WithBrowserClient()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "volvo_page.html"));
        var factory = new StubHttpClientFactory().Html(PageUrl, html);

        var entries = await new VolvoRescueCardSource(factory, NullLogger<VolvoRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        Assert.Equal(6, entries.Count);
        Assert.All(factory.Requests, r => Assert.Equal(RettungskartenHttpClient.BrowserName, r.ClientName));

        var xc60 = Assert.Single(entries, e => e.Parsed.ChassisCode == "D");
        Assert.Equal("XC60", xc60.Parsed.ModelName);
        Assert.Equal((2009, 2017), (xc60.Parsed.BuildYearFrom!.Value, xc60.Parsed.BuildYearTo!.Value));

        var s60 = Assert.Single(entries, e => e.Parsed.ModelName == "S60");
        Assert.Equal(2001, s60.Parsed.BuildYearFrom);
        Assert.Equal(2009, s60.Parsed.BuildYearTo);

        var es90 = Assert.Single(entries, e => e.Parsed.ModelName == "ES90");
        Assert.Equal("Hatchback", es90.Parsed.BodyType);
        Assert.Equal(5, es90.Parsed.Doors);
        Assert.Equal(2027, es90.Parsed.BuildYearFrom);
    }

    [Theory]
    [InlineData("Volvo S90 Mild-Hybrid Typ P 2020", "Volvo_S90-Mild-Hybrid_Typ_P_2020.pdf", "S90", "P", "Mild-Hybrid", 2020, null)]
    [InlineData("Volvo EX40 Fully Electric_Typ X 2024 BEV", "Volvo_EX40_Fully_Electric_Typ_X_2024_BEV.pdf", "EX40", "X", "BEV", 2024, null)]
    [InlineData("Volvo XC60 Typ U 2017 Plug-In Hybrid", "Volvo_XC60_Typ_U_2017_Plug-In-Hybrid.pdf", "XC60", "U", "Plug-in Hybrid", 2017, null)]
    [InlineData("Volvo EX30 Typ 2 2024 BEV", "Volvo_EX30_Typ_2_2024_BEV.pdf", "EX30", "2", "BEV", 2024, null)]
    public void Parser_HandlesRealLabels(string label, string file, string model, string chassis, string fuel, int from, int? to)
    {
        var parsed = VolvoLabelParser.Parse(label, file);

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(chassis, parsed.ChassisCode);
        Assert.Equal(fuel, parsed.FuelType);
        Assert.Equal(from, parsed.BuildYearFrom);
        Assert.Equal(to, parsed.BuildYearTo);
        Assert.Equal("DE", parsed.LanguageCode);
    }
}
