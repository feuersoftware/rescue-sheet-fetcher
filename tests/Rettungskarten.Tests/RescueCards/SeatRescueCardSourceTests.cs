using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Infrastructure.RescueCards;

namespace Rettungskarten.Tests.RescueCards;

public sealed class SeatRescueCardSourceTests
{
    [Fact]
    public async Task DiscoverAsync_FindsGeneralGuideAndModelPagePdfs()
    {
        const string overviewUrl = "https://www.seat.de/kontakt/downloads/rettungsblaetter";
        const string modelPageUrl = "https://www.seat.de/kontakt/downloads/rettungsblaetter/ateca";

        var overviewHtml = await File.ReadAllTextAsync(Path.Combine("Fixtures", "seat_overview.html"));
        var modelPageHtml = await File.ReadAllTextAsync(Path.Combine("Fixtures", "seat_model_page.html"));
        var factory = new MultiResponseHttpClientFactory(new Dictionary<string, string>
        {
            [overviewUrl] = overviewHtml,
            [modelPageUrl] = modelPageHtml
        });

        var source = new SeatRescueCardSource(factory, NullLogger<SeatRescueCardSource>.Instance);
        var entries = await source.DiscoverAsync(CancellationToken.None);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Parsed.ModelName == "General Guide");

        var ateca = Assert.Single(entries, e => e.Parsed.ModelName == "Ateca");
        Assert.Equal("SUV", ateca.Parsed.BodyType);
        Assert.Equal("DE", ateca.Parsed.LanguageCode);
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
}
