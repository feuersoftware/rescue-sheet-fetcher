using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Infrastructure.Stock;

namespace Rettungskarten.Tests.Stock;

/// <summary>
/// KbaStockXlsxParserTests already covers the parser itself against a real downloaded file - this
/// covers the part nothing previously tested directly: resolving the real download URL (including its
/// unpredictable "v=" query parameter) from the KBA product page, and picking the link for the
/// *requested* year out of several candidates rather than just the first one found.
/// </summary>
public sealed class KbaVehicleStockSourceTests
{
    private const string ProductPageUrl =
        "https://www.kba.de/DE/Statistik/Produktkatalog/produkte/Fahrzeuge/fz12_b_uebersicht.html";

    [Fact]
    public async Task FetchWithRawAsync_ResolvesDownloadUrlForRequestedYear_NotJustFirstLink()
    {
        // kba_product_page.html deliberately lists the *wrong* year (2025) before the requested one
        // (2026) - a naive "just take the first a.c-publication link" implementation would return the
        // 2025 URL here and fail this test's assertion, which is the point: this proves the source
        // actually filters by year rather than merely finding a plausible-looking link.
        var productPageHtml = await File.ReadAllTextAsync(Path.Combine("Fixtures", "kba_product_page.html"));
        var xlsxBytes = await File.ReadAllBytesAsync(Path.Combine("Fixtures", "fz12_2026.xlsx"));
        const string expectedDownloadUrl =
            "https://www.kba.de/SharedDocs/Downloads/DE/Statistik/Fahrzeuge/FZ12/fz12_2026.xlsx?__blob=publicationFile&v=7";

        var factory = new StubHttpClientFactory(productPageHtml, expectedDownloadUrl, xlsxBytes);
        var source = new KbaVehicleStockSource(factory, NullLogger<KbaVehicleStockSource>.Instance);

        var result = await source.FetchWithRawAsync(2026, CancellationToken.None);

        Assert.Equal(expectedDownloadUrl, result.Parsed.SourceUrl);
        Assert.Equal(2026, result.Parsed.Year);
        Assert.True(result.Parsed.Rows.Count > 600, $"Expected >600 model rows, got {result.Parsed.Rows.Count}");
        Assert.Equal("fz12_2026.xlsx", result.RawFileName);
    }

    [Fact]
    public async Task FetchWithRawAsync_ProductPageUnreachable_ThrowsInvalidOperationExceptionNotRawHttpException()
    {
        // Regression test: unlike every brand source (which routes through HttpDownloadHelper),
        // KbaVehicleStockSource called client.GetStringAsync/GetByteArrayAsync directly, so a KBA
        // outage surfaced as a raw HttpRequestException - uncaught by FetchStockCommand's existing
        // catch (NotSupportedException)/(InvalidOperationException) clauses, producing an unhandled
        // stack trace instead of a clean Stock_ErrorPrefix-style message.
        var factory = new FailingHttpClientFactory();
        var source = new KbaVehicleStockSource(factory, NullLogger<KbaVehicleStockSource>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.FetchWithRawAsync(2026, CancellationToken.None));
        Assert.Contains(ProductPageUrl, ex.Message);
    }

    private sealed class FailingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler());

        private sealed class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                throw new HttpRequestException("simulated network failure");
        }
    }

    private sealed class StubHttpClientFactory(string productPageHtml, string expectedDownloadUrl, byte[] xlsxBytes) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(productPageHtml, expectedDownloadUrl, xlsxBytes));

        private sealed class StubHandler(string productPageHtml, string expectedDownloadUrl, byte[] xlsxBytes) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var url = request.RequestUri!.ToString();
                if (url == ProductPageUrl)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(productPageHtml) });
                }

                if (url == expectedDownloadUrl)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(xlsxBytes) });
                }

                throw new InvalidOperationException($"No stub response configured for {url}");
            }
        }
    }
}
