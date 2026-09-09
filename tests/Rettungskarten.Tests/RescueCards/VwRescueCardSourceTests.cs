using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Infrastructure.RescueCards;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Regression test for a real bug: the feed's flat file list omits the language bucket's own name as
/// a path segment, so naively joining base_url + fileName produced a URL the S3 bucket rejects with
/// "AccessDenied" (looking exactly like an auth/session problem) instead of the real
/// base_url/languageBucketName/fileName key. Found by intercepting the network request VW's own
/// rescue-data widget makes when downloading a card manually.
/// </summary>
public sealed class VwRescueCardSourceTests
{
    private const string FeedJson = """
        {
          "config": { "base_url": "https://assets.example/rescue-asset/vw-de/pdf/" },
          "languages": [
            { "name": "language01.DE", "files": ["Volkswagen_Golf_Cabrio_1993_2d_GD_DE.pdf"] },
            { "name": "language02.EN", "files": ["Volkswagen_Golf_Cabrio_1993_2d_GD_EN.pdf"] }
          ]
        }
        """;

    [Fact]
    public async Task DiscoverAsync_DownloadUrlIncludesLanguageBucketNameSegment()
    {
        var factory = new StubHttpClientFactory(FeedJson);
        var source = new VwRescueCardSource(factory, NullLogger<VwRescueCardSource>.Instance);

        var entries = await source.DiscoverAsync(CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal(
            "https://assets.example/rescue-asset/vw-de/pdf/language01.DE/Volkswagen_Golf_Cabrio_1993_2d_GD_DE.pdf",
            entry.DownloadUrl);
    }

    private sealed class StubHttpClientFactory(string responseJson) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(responseJson));

        private sealed class StubHandler(string responseJson) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseJson) });
        }
    }
}
