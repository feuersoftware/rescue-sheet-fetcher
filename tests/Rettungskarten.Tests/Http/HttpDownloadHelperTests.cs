using System.Globalization;
using System.Net;
using Rettungskarten.Core.Localization;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.Http;

public class HttpDownloadHelperTests : IDisposable
{
    public void Dispose() => Strings.OverrideCulture = null;

    [Fact]
    public async Task DownloadPdfAsync_RealPdfServedAsOctetStream_IsAccepted()
    {
        // Hyundai's Scene7 links have no .pdf extension and some CDNs label PDFs as octet-stream -
        // the %PDF signature, not the Content-Type, decides.
        var factory = new StubHttpClientFactory().Bytes("https://cdn.test/sheet", StubHttpClientFactory.FakePdf(), "application/octet-stream");

        var result = await HttpDownloadHelper.DownloadPdfAsync(factory.CreateClient("x"), "https://cdn.test/sheet", CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task DownloadPdfAsync_HtmlWith200_IsRejected()
    {
        // smart's site answers a missing file with 200 and an HTML page.
        var factory = new StubHttpClientFactory().Html("https://cdn.test/missing.pdf", "<html>not found</html>");

        var result = await HttpDownloadHelper.DownloadPdfAsync(factory.CreateClient("x"), "https://cdn.test/missing.pdf", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task DownloadPdfAsync_PdfContentTypeButNoSignature_IsRejected()
    {
        var factory = new StubHttpClientFactory().Bytes("https://cdn.test/broken.pdf", "<html/>"u8.ToArray());

        var result = await HttpDownloadHelper.DownloadPdfAsync(factory.CreateClient("x"), "https://cdn.test/broken.pdf", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task DownloadPdfAsync_RobotsBlockedResponse_ReportsLocalizedRobotsReason()
    {
        Strings.OverrideCulture = new CultureInfo("en");
        var factory = new StubHttpClientFactory().Respond("https://cdn.test/card.pdf", _ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)451);
            response.Headers.Add(RobotsTxtDelegatingHandler.BlockedMarkerHeader, "robots.txt");
            return response;
        });

        var result = await HttpDownloadHelper.DownloadPdfAsync(factory.CreateClient("x"), "https://cdn.test/card.pdf", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(451, result.HttpStatusCode);
        Assert.Equal(Strings.Get("FailureReason_RobotsTxtDisallowed"), result.FailureReason);
    }

    [Theory]
    [InlineData("%PDF-1.7", true)]
    [InlineData("\r\n%PDF-1.4", true)]
    [InlineData("<!DOCTYPE html>", false)]
    [InlineData("", false)]
    public void LooksLikePdf(string start, bool expected) =>
        Assert.Equal(expected, HttpDownloadHelper.LooksLikePdf(System.Text.Encoding.ASCII.GetBytes(start)));

    [Fact]
    public void ResolveUrl_EscapesUmlautsAndSpacesExactlyOnce()
    {
        var resolved = HttpDownloadHelper.ResolveUrl(
            "https://www.toyota.de/service/rettungsdatenblätter", "/files/Rettungsdatenblatt Yaris (2020).pdf");

        Assert.Equal("https://www.toyota.de/files/Rettungsdatenblatt%20Yaris%20(2020).pdf", resolved);
        Assert.Equal(resolved, HttpDownloadHelper.ResolveUrl("https://example.test/", resolved));
        Assert.Equal("https://www.toyota.de/rettungsdatenbl%C3%A4tter", HttpDownloadHelper.NormalizeUrl("https://www.toyota.de/rettungsdatenblätter"));
    }

    [Fact]
    public void GetFileName_DecodesTheLastPathSegmentAndDropsTheQuery()
    {
        Assert.Equal("Yaris (2020).pdf", HttpDownloadHelper.GetFileName("https://cdn.test/a/Yaris%20(2020).pdf?v=3"));
        Assert.Equal("sheet", HttpDownloadHelper.GetFileName("https://cdn.test/a/sheet"));
    }
}
