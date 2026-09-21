using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Infrastructure.RescueCards;

namespace Rettungskarten.Tests.RescueCards;

public sealed class CupraRescueCardSourceTests
{
    [Fact]
    public async Task DiscoverAsync_FiltersToGermanOnly_UsesSharedFilenameParser()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "cupra_page.html"));
        var factory = new SingleResponseHttpClientFactory(html);
        var source = new CupraRescueCardSource(factory, NullLogger<CupraRescueCardSource>.Instance);

        var entries = await source.DiscoverAsync(CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal("Formentor", entry.Parsed.ModelName);
        Assert.Equal("Hybrid-Electric", entry.Parsed.FuelType);
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
