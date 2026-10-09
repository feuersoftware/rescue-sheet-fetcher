using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

public class HtmlPdfLinkRescueCardSourceTests
{
    private const string PageUrl = "https://www.example-brand.test/service/rettungskarten.html";

    private const string Html = """
        <html><body>
          <a href="/files/Example_Alpha_Schragheck_2020_5d_Benzin_DE.pdf">Alpha</a>
          <a href="/files/Example_Alpha_Schragheck_2020_5d_Benzin_DE.pdf" title="Alpha (teaser)">Teaser</a>
          <a href="/files/example_beta_rettungsdatenblatt.pdf" aria-label="Beta Kombi (ab 2018)">PDF</a>
          <a href="/files/Example_Gamma_SUV_2022_5d_Elektro_EN.pdf">Gamma EN</a>
          <a href="/files/Example_Gamma_SUV_2022_5d_Elektro_DE.pdf">Gamma DE</a>
          <a href="/files/Example_Delta_SUV_2023_5d_Elektro_FR.pdf">Delta FR only</a>
          <a href="/files/ERG_Example_Gamma.pdf">Emergency Response Guide Gamma</a>
          <a href="/files/legende.pdf">Legende</a>
          <a href="/modelle/alpha.html">Alpha model page</a>
          <a href="mailto:service@example-brand.test">Mail</a>
        </body></html>
        """;

    [Fact]
    public async Task DiscoverAsync_CollectsRescueSheetLinks_WithDefaultsAppliedOnce()
    {
        var factory = new StubHttpClientFactory().Html(PageUrl, Html);
        var source = new ExampleSource(factory);

        var entries = await source.DiscoverAsync(CancellationToken.None);

        // Alpha (deduplicated), Beta (label-parsed), Gamma (DE preferred over EN); Delta (French only),
        // the ERG, the legend and the non-PDF links are dropped.
        Assert.Equal(["Alpha", "Beta", "Gamma"], entries.Select(e => e.Parsed.ModelName).Order());
        Assert.All(entries, e => Assert.Equal(Brand.Honda, e.Brand));
        Assert.All(entries, e => Assert.Equal(PageUrl, e.SourcePageUrl));

        var beta = Assert.Single(entries, e => e.Parsed.ModelName == "Beta");
        Assert.Equal("https://www.example-brand.test/files/example_beta_rettungsdatenblatt.pdf", beta.DownloadUrl);
        Assert.Equal("Kombi", beta.Parsed.BodyType);
        Assert.Equal(2018, beta.Parsed.BuildYearFrom);
        Assert.Equal("DE", beta.Parsed.LanguageCode);

        var gamma = Assert.Single(entries, e => e.Parsed.ModelName == "Gamma");
        Assert.Equal("DE", gamma.Parsed.LanguageCode);
    }

    [Fact]
    public async Task DiscoverAsync_UsesTheConfiguredDiscoveryClient()
    {
        var factory = new StubHttpClientFactory().Html(PageUrl, Html);

        await new ExampleSource(factory).DiscoverAsync(CancellationToken.None);

        Assert.All(factory.Requests, r => Assert.Equal(RettungskartenHttpClient.BrowserName, r.ClientName));
    }

    private sealed class ExampleSource(IHttpClientFactory factory)
        : HtmlPdfLinkRescueCardSource(factory, NullLogger.Instance)
    {
        public override Brand Brand => Brand.Honda;

        protected override IReadOnlyList<string> PageUrls => [PageUrl];

        protected override string DiscoveryClientName => RettungskartenHttpClient.BrowserName;
    }
}
