using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// Shared "download a PDF, turn HTTP/network failures into a result instead of an exception" logic
/// used by every brand source, since a single model's download failing (e.g. Cupra AT's auth gateway
/// redirect) must never bubble up as an exception - see <c>IRescueCardSource.DownloadAsync</c>.
///
/// What counts as "a PDF" is decided by the content, not the headers: the file must start with the
/// <c>%PDF-</c> signature (within the first 1KB, where the spec allows it). Headers alone were wrong
/// both ways - some hosts serve real PDFs as <c>application/octet-stream</c> or with no extension at
/// all (Hyundai's Scene7 links), while smart's site answers a missing file with 200 and an HTML page.
/// The Content-Type is still used to explain a rejection (an HTML login page vs. something else).
/// </summary>
public static class HttpDownloadHelper
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private const int PdfSignatureSearchWindow = 1024;

    public static Task<RescueCardDownloadResult> DownloadPdfAsync(HttpClient client, string url, CancellationToken ct) =>
        DownloadPdfAsync(client, url, configureRequest: null, ct);

    public static async Task<RescueCardDownloadResult> DownloadPdfAsync(
        HttpClient client, string url, Action<HttpRequestMessage>? configureRequest, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, NormalizeUrl(url));
            configureRequest?.Invoke(request);
            using var response = await client.SendAsync(request, ct);

            if (RobotsTxtDelegatingHandler.IsBlockedResponse(response))
            {
                return RescueCardDownloadResult.Fail(Strings.Get("FailureReason_RobotsTxtDisallowed"), (int)response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                return RescueCardDownloadResult.Fail(
                    Strings.Get("Http_UnexpectedStatusCode", (int)response.StatusCode, response.ReasonPhrase ?? string.Empty),
                    (int)response.StatusCode);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            if (!LooksLikePdf(bytes))
            {
                // Some gated downloads (e.g. Cupra AT) return 200 with an HTML login/redirect page
                // instead of a real PDF - treat that as a failure rather than saving garbage bytes.
                return RescueCardDownloadResult.Fail(
                    contentType is not null && !contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                        ? Strings.Get("Http_UnexpectedContentType", contentType)
                        : Strings.Get("Http_NotAPdf"));
            }

            return RescueCardDownloadResult.Ok(bytes);
        }
        catch (HttpRequestException ex)
        {
            return RescueCardDownloadResult.Fail(ex.Message, (int?)ex.StatusCode);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return RescueCardDownloadResult.Fail(Strings.Get("Http_Timeout", ex.Message));
        }
    }

    public static bool LooksLikePdf(ReadOnlySpan<byte> content)
    {
        var window = content[..Math.Min(content.Length, PdfSignatureSearchWindow)];
        return window.IndexOf(PdfSignature) >= 0;
    }

    /// <summary>
    /// Resolves a possibly relative link against the page it was found on and returns it in escaped
    /// form (<see cref="Uri.AbsoluteUri"/>), so umlauts ("rettungsdatenblätter"), spaces and other
    /// characters manufacturers put in paths are percent-encoded exactly once. A literal <c>#</c> is
    /// always a fragment here - a source whose filenames contain one (smart) must escape it as
    /// <c>%23</c> itself before calling this.
    /// </summary>
    public static string ResolveUrl(string baseUrl, string possiblyRelativeUrl)
    {
        var trimmed = possiblyRelativeUrl.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) && absolute.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? absolute.AbsoluteUri
            : new Uri(new Uri(baseUrl), trimmed).AbsoluteUri;
    }

    /// <summary>Escapes an already-absolute URL (see <see cref="ResolveUrl"/>); anything that isn't a
    /// valid absolute URL is returned unchanged for HttpClient to reject with its own error.</summary>
    public static string NormalizeUrl(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ? uri.AbsoluteUri : url;

    /// <summary>The last path segment of a URL, percent-decoded - the "filename" most sources' metadata
    /// is parsed from.</summary>
    public static string GetFileName(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url.Split('?', '#')[0];
        var lastSegment = path.TrimEnd('/').Split('/')[^1];
        return Uri.UnescapeDataString(lastSegment);
    }
}
