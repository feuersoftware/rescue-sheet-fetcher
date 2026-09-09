using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Infrastructure.RescueCards;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixture (bentley_page.html) is a trimmed real slice of the real page: three rescue-sheet model
/// items, each using a *different* per-language URL naming scheme actually seen on the real site (the
/// reason this source filters on the link's own "DEUTSCHE" button label instead of any URL shape - see
/// BentleyRescueCardSource's doc comment), plus one decoy accordion item with no year in its heading
/// and a "DEUTSCHE"-labelled link anyway, to lock in that non-model sections get excluded.
/// </summary>
public sealed class BentleyRescueCardSourceTests
{
    [Fact]
    public async Task DiscoverAsync_FindsGermanLinkAcrossDifferentUrlSchemes()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "bentley_page.html"));
        var factory = new StubHttpClientFactory(html);
        var source = new BentleyRescueCardSource(factory, NullLogger<BentleyRescueCardSource>.Instance);

        var entries = await source.DiscoverAsync(CancellationToken.None);

        Assert.Equal(2, entries.Count); // MULSANNE has no German link and must be skipped

        var continental = Assert.Single(entries, e => e.Parsed.ModelName == "NEW CONTINENTAL GT");
        Assert.Equal("https://cdn.bentleymotors.com/downloads/de/bm/rc/634_DE_Rescue_Card_V1.pdf", continental.DownloadUrl);
        Assert.Equal(2018, continental.Parsed.BuildYearFrom);
        Assert.Equal(2024, continental.Parsed.BuildYearTo);
        Assert.Equal("ICE", continental.Parsed.FuelType);

        var bentayga = Assert.Single(entries, e => e.Parsed.ModelName == "BENTAYGA");
        Assert.Equal(
            "https://cdn.bentleymotors.com/downloads/uk/bm/cert/SCB_TSD_13046_Bentayga2_RESCUE_SHEETS-German-Aug2020.pdf",
            bentayga.DownloadUrl);
        Assert.Equal(2020, bentayga.Parsed.BuildYearFrom);
        Assert.Null(bentayga.Parsed.BuildYearTo);
    }

    [Fact]
    public async Task DiscoverAsync_SkipsModelWhoseGermanLinkSharesUrlWithAnotherLanguage()
    {
        // Regression test for a real bug found on the live Bentley site: "BENTAYGA (HYBRID) (2021 - )"
        // has its "DEUTSCHE" button pointing at the exact same URL as its "中文" button, whose filename
        // literally says "chinese_simplified" - a mislabeling bug on Bentley's own page. Trusting the
        // label there would silently hand out a Chinese PDF as a German rescue card.
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "bentley_page.html"));
        var factory = new StubHttpClientFactory(html);
        var source = new BentleyRescueCardSource(factory, NullLogger<BentleyRescueCardSource>.Instance);

        var entries = await source.DiscoverAsync(CancellationToken.None);

        Assert.DoesNotContain(entries, e => e.Parsed.Variant == "BENTAYGA (HYBRID) (2021 - )");
    }

    [Fact]
    public async Task DiscoverAsync_SkipsNonModelAccordionItems()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "bentley_page.html"));
        var factory = new StubHttpClientFactory(html);
        var source = new BentleyRescueCardSource(factory, NullLogger<BentleyRescueCardSource>.Instance);

        var entries = await source.DiscoverAsync(CancellationToken.None);

        Assert.DoesNotContain(entries, e => e.Parsed.ModelName!.Contains("Technical Data"));
    }

    private sealed class StubHttpClientFactory(string html) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(html));

        private sealed class StubHandler(string html) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        }
    }
}
