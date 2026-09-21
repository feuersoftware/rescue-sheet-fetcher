using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Infrastructure.RescueCards;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// PorscheDocumentsPageParserTests already covers link extraction from the real fixture page in
/// isolation - this test exercises the full DiscoverAsync path (HTTP fetch + parsing + the
/// "Classic" vs. current-models ModelName split) that no test previously covered directly.
/// </summary>
public sealed class PorscheRescueCardSourceTests
{
    [Fact]
    public async Task DiscoverAsync_SplitsCurrentAndClassicDocuments_EnglishLanguage()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "porsche_documents_page.html"));
        var factory = new SingleResponseHttpClientFactory(html);
        var source = new PorscheRescueCardSource(factory, NullLogger<PorscheRescueCardSource>.Instance);

        var entries = await source.DiscoverAsync(CancellationToken.None);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Parsed.ModelName == "All Models");
        Assert.Contains(entries, e => e.Parsed.ModelName == "All Models Classic");
        Assert.All(entries, e => Assert.Equal("EN", e.Parsed.LanguageCode));
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
