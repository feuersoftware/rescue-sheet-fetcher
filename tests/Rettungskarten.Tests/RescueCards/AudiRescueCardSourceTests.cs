using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Infrastructure.RescueCards;

namespace Rettungskarten.Tests.RescueCards;

public sealed class AudiRescueCardSourceTests
{
    [Fact]
    public async Task DiscoverAsync_FiltersToGermanOnly_UsesAudiFilenameParser()
    {
        // The German entry's filename ("..._Hybrid_(Electric)_DE.pdf") is one of Audi's own real CMS
        // quirks (see AudiFilenameParser's doc comment) - this exercises that quirk-normalization
        // end-to-end via DiscoverAsync, not just AudiFilenameParserTests' isolated unit tests. A source
        // that mistakenly called the shared VwSeatCupraFilenameParser instead would get this wrong
        // (BodyType would come back "2018", not "Limousine" - see that parser's doc comment for why).
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "audi_page.html"));
        var factory = new SingleResponseHttpClientFactory(html);
        var source = new AudiRescueCardSource(factory, NullLogger<AudiRescueCardSource>.Instance);

        var entries = await source.DiscoverAsync(CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal("A6", entry.Parsed.ModelName);
        Assert.Equal("Limousine", entry.Parsed.BodyType);
        Assert.Equal(2018, entry.Parsed.BuildYearFrom);
        Assert.Equal("Hybrid (Electric)", entry.Parsed.FuelType);
        Assert.Equal("DE", entry.Parsed.LanguageCode);
        Assert.EndsWith("_DE.pdf", entry.DownloadUrl);
    }

    private sealed class SingleResponseHttpClientFactory(string html) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(html));

        private sealed class StubHandler(string html) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        }
    }
}
