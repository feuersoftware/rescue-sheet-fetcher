using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace Rettungskarten.Tests.TestSupport;

/// <summary>
/// Shared stub <see cref="IHttpClientFactory"/> for source tests: answers each request from a table of
/// canned responses keyed by the exact request URL (as <see cref="Uri.AbsoluteUri"/>, i.e. escaped),
/// and records every request (with the named client it came from) for assertions. An unknown URL
/// throws, so a test never silently passes against an unexpected request.
/// </summary>
public sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(StringComparer.Ordinal);

    public ConcurrentQueue<(string ClientName, HttpRequestMessage Request)> Requests { get; } = new();

    public StubHttpClientFactory Html(string url, string body) =>
        Respond(url, _ => Text(body, "text/html"));

    public StubHttpClientFactory Json(string url, string body) =>
        Respond(url, _ => Text(body, "application/json"));

    public StubHttpClientFactory Bytes(string url, byte[] body, string contentType = "application/pdf") =>
        Respond(url, _ =>
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

    public StubHttpClientFactory Status(string url, HttpStatusCode status) =>
        Respond(url, _ => new HttpResponseMessage(status) { Content = new StringContent(string.Empty) });

    public StubHttpClientFactory Respond(string url, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _responses[new Uri(url).AbsoluteUri] = respond;
        return this;
    }

    /// <summary>A minimal byte sequence that passes the downloader's %PDF signature check.</summary>
    public static byte[] FakePdf() => "%PDF-1.4\n%fake\n"u8.ToArray();

    public HttpClient CreateClient(string name) => new(new Handler(this, name));

    private static HttpResponseMessage Text(string body, string mediaType) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType) };

    private sealed class Handler(StubHttpClientFactory owner, string clientName) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            owner.Requests.Enqueue((clientName, request));
            var url = request.RequestUri!.AbsoluteUri;
            if (!owner._responses.TryGetValue(url, out var respond))
            {
                throw new InvalidOperationException($"No stub response configured for {url}");
            }

            var response = respond(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
