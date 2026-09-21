using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards;

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
