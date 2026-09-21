using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Infrastructure.RescueCards;

namespace Rettungskarten.Tests.RescueCards;

public sealed class LamborghiniRescueCardSourceTests
{
    [Fact]
    public async Task DiscoverAsync_ExcludesCombinedGuide_ExtractsModelNamesFromFileNames()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "lamborghini_page.html"));
        var factory = new StubHttpClientFactory(html);
        var source = new LamborghiniRescueCardSource(factory, NullLogger<LamborghiniRescueCardSource>.Instance);

        var entries = await source.DiscoverAsync(CancellationToken.None);

        // 4 links in the fixture, but "EMERGENCY RESPONSE GUIDE" doesn't say "Rescue Data Sheet" and
        // must be excluded, leaving exactly the 3 per-model sheets.
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Parsed.ModelName == "REVUELTO");
        Assert.Contains(entries, e => e.Parsed.ModelName == "URUS SE");
        Assert.Contains(entries, e => e.Parsed.ModelName == "TEMERARIO");
        Assert.All(entries, e => Assert.Equal("EN", e.Parsed.LanguageCode));
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
