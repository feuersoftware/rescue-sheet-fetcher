using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

public class RescueCardSourceBaseTests
{
    [Fact]
    public async Task DownloadAsync_NullDownloadUrl_ThrowsArgumentNullExceptionInsteadOfDeepFailure()
    {
        // Regression test: DownloadAsync used to dereference entry.DownloadUrl with the null-forgiving
        // operator (!) in every brand source, relying entirely on RescueCardOrchestrator already
        // checking DownloadUrl for null before calling DownloadAsync - the only actual protection. A
        // future caller that doesn't replicate that check (a retry command, a direct unit test, a debug
        // tool) should get a clear validation error at the point of misuse, not a confusing low-level
        // exception from deep inside HttpClient.
        var source = new StubSource();
        var parsed = new ParsedModelInfo(
            ModelName: null, Variant: null, BodyType: null, BuildYearFrom: null, BuildYearTo: null,
            Doors: null, FuelType: null, LanguageCode: null, ParseConfidence: ParseConfidence.Unparsed);
        var entry = new RescueCardEntry(Brand.VW, "https://example.test", DownloadUrl: null, "x.pdf", parsed);

        await Assert.ThrowsAsync<ArgumentNullException>(() => source.DownloadAsync(entry, CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_ResolvesTheRealPdfUrlAtDownloadTime()
    {
        // BMW's signed S3 URLs expire after 2h and Mercedes' PDF link lives on a per-card detail page -
        // the entry keeps the stable URL, the source resolves the real one only when downloading.
        var factory = new StubHttpClientFactory()
            .Html("https://portal.test/card/42", "<a href='/files/42.pdf'>PDF</a>")
            .Bytes("https://portal.test/files/42.pdf", StubHttpClientFactory.FakePdf());
        var source = new ResolvingSource(factory);

        var result = await source.DownloadAsync(Entry("https://portal.test/card/42"), CancellationToken.None);

        Assert.True(result.Success);
        var pdfRequest = factory.Requests.Last();
        Assert.Equal("https://portal.test/files/42.pdf", pdfRequest.Request.RequestUri!.AbsoluteUri);
        Assert.All(factory.Requests, r => Assert.Equal(RettungskartenHttpClient.BrowserName, r.ClientName));
    }

    [Fact]
    public async Task DownloadAsync_ResolutionFindsNothing_IsAFailedResultNotAnException()
    {
        var factory = new StubHttpClientFactory().Html("https://portal.test/card/43", "<p>no link</p>");

        var result = await new ResolvingSource(factory).DownloadAsync(Entry("https://portal.test/card/43"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task DownloadAsync_ResolutionHttpFailure_IsAFailedResultNotAnException()
    {
        var factory = new StubHttpClientFactory().Status("https://portal.test/card/44", System.Net.HttpStatusCode.NotFound);

        var result = await new ResolvingSource(factory).DownloadAsync(Entry("https://portal.test/card/44"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(404, result.HttpStatusCode);
    }

    private static RescueCardEntry Entry(string url) => new(
        Brand.MercedesBenz, "https://portal.test/", url, "card",
        new ParsedModelInfo(null, null, null, null, null, null, null, null, ParseConfidence.Unparsed));

    private sealed class ResolvingSource(IHttpClientFactory factory) : RescueCardSourceBase(factory)
    {
        public override Brand Brand => Brand.MercedesBenz;

        protected override string DownloadClientName => RettungskartenHttpClient.BrowserName;

        public override Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RescueCardEntry>>([]);

        protected override async Task<string?> ResolveDownloadUrlAsync(RescueCardEntry entry, HttpClient client, CancellationToken ct)
        {
            var html = await client.GetStringAsync(entry.DownloadUrl, ct);
            var start = html.IndexOf("href='", StringComparison.Ordinal);
            return start < 0 ? null : HttpDownloadHelper.ResolveUrl(entry.DownloadUrl!, html[(start + 6)..html.IndexOf('\'', start + 6)]);
        }
    }

    private sealed class StubSource() : RescueCardSourceBase(new NoopHttpClientFactory())
    {
        public override Brand Brand => Brand.VW;

        public override Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RescueCardEntry>>([]);
    }

    private sealed class NoopHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
