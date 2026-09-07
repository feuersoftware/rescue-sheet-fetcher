using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// Shared "download a PDF, turn HTTP/network failures into a result instead of an exception" logic
/// used by every brand source, since a single model's download failing (VW's 403, Cupra AT's auth
/// gateway redirect) must never bubble up as an exception - see <c>IRescueCardSource.DownloadAsync</c>.
/// </summary>
public static class HttpDownloadHelper
{
    public static async Task<RescueCardDownloadResult> DownloadPdfAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return RescueCardDownloadResult.Fail($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", (int)response.StatusCode);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            if (contentType is not null && !contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            {
                // Some gated downloads (e.g. Cupra AT) return 200 with an HTML login/redirect page
                // instead of a real PDF - treat that as a failure rather than saving garbage bytes.
                return RescueCardDownloadResult.Fail($"Unerwarteter Content-Type '{contentType}' statt PDF (evtl. Auth-Gateway-Weiterleitung)");
            }

            return RescueCardDownloadResult.Ok(bytes);
        }
        catch (HttpRequestException ex)
        {
            return RescueCardDownloadResult.Fail(ex.Message, (int?)ex.StatusCode);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return RescueCardDownloadResult.Fail($"Timeout: {ex.Message}");
        }
    }

    public static string ResolveUrl(string baseUrl, string possiblyRelativeUrl) =>
        possiblyRelativeUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? possiblyRelativeUrl
            : new Uri(new Uri(baseUrl), possiblyRelativeUrl).ToString();
}
