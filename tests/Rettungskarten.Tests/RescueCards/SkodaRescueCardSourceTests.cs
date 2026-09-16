using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Infrastructure.RescueCards;

namespace Rettungskarten.Tests.RescueCards;

public class SkodaRescueCardSourceTests
{
    [Fact]
    public async Task DiscoverAsync_ModelPageLinkIsRelative_ResolvesToAbsoluteDownloadUrl()
    {
        // Regression test: unlike the overview page's own link-resolution loop, the per-model
        // "download-file" module's file.Link was stored as DownloadUrl without going through
        // HttpDownloadHelper.ResolveUrl - a relative link (as this fixture deliberately uses) would
        // otherwise be passed straight to HttpClient.GetAsync, which throws for a non-absolute URI.
        const string overviewUrl = "https://www.skoda-auto.de/service/rettungskraefte";
        const string modelPageUrl = "https://www.skoda-auto.de/service/rettungskraefte/rettungsdatenblatt-fabia";

        var overviewHtml = await File.ReadAllTextAsync(Path.Combine("Fixtures", "skoda_overview.html"));
        var modelPageHtml = await File.ReadAllTextAsync(Path.Combine("Fixtures", "skoda_model_page.html"));
        var factory = new MultiResponseHttpClientFactory(new Dictionary<string, string>
        {
            [overviewUrl] = overviewHtml,
            [modelPageUrl] = modelPageHtml
        });

        var source = new SkodaRescueCardSource(factory, NullLogger<SkodaRescueCardSource>.Instance);
        var entries = await source.DiscoverAsync(CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal("Fabia", entry.Parsed.ModelName);
        Assert.Equal("https://www.skoda-auto.de/media/rettungsdatenblatt-fabia.pdf", entry.DownloadUrl);
    }

    private sealed class MultiResponseHttpClientFactory(IReadOnlyDictionary<string, string> responsesByUrl) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(responsesByUrl));

        private sealed class StubHandler(IReadOnlyDictionary<string, string> responsesByUrl) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var url = request.RequestUri!.ToString();
                if (!responsesByUrl.TryGetValue(url, out var body))
                {
                    throw new InvalidOperationException($"No stub response configured for {url}");
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            }
        }
    }

    // Real model-page titles found during a production run. "iV" is Škoda's lower-case-i electric-trim
    // badge (e.g. "Citigo-e iV") - it must not be confused with the upper-case-I Roman-numeral
    // generation marker used throughout these same titles ("Fabia IV", "Octavia IV"), which is never a
    // fuel type on its own.
    [Theory]
    [InlineData("Škoda Fabia IV (ab 2021)", null)]
    [InlineData("Škoda Octavia III (ab 2012)", null)]
    [InlineData("Škoda Citigo-e iV (ab 2019)", "iV")]
    [InlineData("Škoda Octavia IV CNG (ab 2020)", "CNG")]
    [InlineData("Škoda Superb iV PHEV HYBRID (ab 2024)", "PHEV HYBRID")]
    public void ExtractFuelType_DistinguishesElectricTrimBadgeFromGenerationNumeral(string title, string? expectedFuelType)
    {
        Assert.Equal(expectedFuelType, SkodaRescueCardSource.ExtractFuelType(title));
    }
}
