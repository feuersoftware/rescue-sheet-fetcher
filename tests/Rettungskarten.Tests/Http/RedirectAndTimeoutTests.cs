using System.Net;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Rettungskarten.Infrastructure.Http;

namespace Rettungskarten.Tests.Http;

public class RedirectFollowerTests
{
    [Fact]
    public async Task FollowsRelativeRedirects_AndReportsTheLastHopAsRequestMessage()
    {
        var sent = new List<string>();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://portal.test/d/abc");

        using var response = await RedirectFollower.SendAsync(request, (hop, _) =>
        {
            sent.Add(hop.RequestUri!.AbsoluteUri);
            return Task.FromResult(hop.RequestUri!.AbsolutePath == "/d/abc"
                ? Redirect(HttpStatusCode.Found, "/files/sheet.pdf", hop)
                : new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = hop });
        }, CancellationToken.None);

        Assert.Equal(["https://portal.test/d/abc", "https://portal.test/files/sheet.pdf"], sent);
        Assert.Equal("https://portal.test/files/sheet.pdf", response.RequestMessage!.RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently, "GET")]
    [InlineData(HttpStatusCode.Found, "GET")]
    [InlineData(HttpStatusCode.SeeOther, "GET")]
    [InlineData(HttpStatusCode.TemporaryRedirect, "POST")]
    [InlineData(HttpStatusCode.PermanentRedirect, "POST")]
    public void PostRedirect_FollowsHttpClientsMethodRules(HttpStatusCode status, string expectedMethod)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://ford.test/ServiceTip/Get") { Content = new StringContent("Year=2025") };

        using var next = RedirectFollower.CreateRedirectRequest(request, Redirect(status, "https://ford.test/ServiceTip/Result", request));

        Assert.NotNull(next);
        Assert.Equal(expectedMethod, next.Method.Method);
        Assert.Equal(expectedMethod == "POST", next.Content is not null);
    }

    [Fact]
    public void HttpsToHttpDowngrade_IsNotFollowed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/a.pdf");

        Assert.Null(RedirectFollower.CreateRedirectRequest(request, Redirect(HttpStatusCode.Found, "http://example.test/a.pdf", request)));
    }

    [Fact]
    public void CookieHeader_IsKeptForTheSameHost_AndDroppedForAnotherHost()
    {
        // Ford sends its market cookie by hand; it must survive a same-host redirect but never leak
        // to another host.
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://ford.test/ServiceTip");
        request.Headers.TryAddWithoutValidation("Cookie", "UserCountry=67");
        request.Headers.TryAddWithoutValidation("Authorization", "secret");
        request.Headers.UserAgent.ParseAdd("RettungskartenTool/1.0");

        using var sameHost = RedirectFollower.CreateRedirectRequest(request, Redirect(HttpStatusCode.Found, "/SetCountry", request))!;
        using var otherHost = RedirectFollower.CreateRedirectRequest(request, Redirect(HttpStatusCode.Found, "https://cdn.test/x.pdf", request))!;

        Assert.True(sameHost.Headers.Contains("Cookie"));
        Assert.False(otherHost.Headers.Contains("Cookie"));
        Assert.False(sameHost.Headers.Contains("Authorization"));
        Assert.Equal("RettungskartenTool/1.0", otherHost.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task RedirectLoop_StopsAfterMaxRedirects()
    {
        var hops = 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://loop.test/a");

        using var response = await RedirectFollower.SendAsync(request, (hop, _) =>
        {
            hops++;
            return Task.FromResult(Redirect(HttpStatusCode.Found, "/a", hop));
        }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(RedirectFollower.MaxRedirects + 1, hops);
    }

    private static HttpResponseMessage Redirect(HttpStatusCode status, string location, HttpRequestMessage request) =>
        new(status) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) }, RequestMessage = request };
}

public class SendTimeoutDelegatingHandlerTests
{
    [Fact]
    public async Task SlowResponse_ThrowsTimeoutRejectedException()
    {
        using var client = new HttpClient(new SendTimeoutDelegatingHandler(TimeSpan.FromMilliseconds(100)) { InnerHandler = new SlowHandler() });

        var ex = await Assert.ThrowsAsync<TimeoutRejectedException>(() => client.GetAsync("https://slow.test/"));

        Assert.True(HttpRequestFailures.IsTimeout(ex, CancellationToken.None));
    }

    [Fact]
    public async Task CallerCancellation_StaysACancellation()
    {
        using var client = new HttpClient(new SendTimeoutDelegatingHandler(TimeSpan.FromSeconds(30)) { InnerHandler = new SlowHandler() });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://slow.test/", cts.Token));
    }

    [Fact]
    public async Task HeadersThenStalledBody_ThrowsTimeoutRejectedException()
    {
        // Regression (PR review): the send timeout ended with the headers, and the clients'
        // HttpClient.Timeout is infinite, so a host that sent headers and then stalled hung the body
        // read forever - including the shared robots.txt fetch every request to that host waits on.
        var handler = new SendTimeoutDelegatingHandler(TimeSpan.FromSeconds(30), bodyTimeout: TimeSpan.FromMilliseconds(100))
        {
            InnerHandler = new StalledBodyHandler()
        };
        using var invoker = new HttpMessageInvoker(handler);

        await Assert.ThrowsAsync<TimeoutRejectedException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://stalled.test/robots.txt"), CancellationToken.None));
    }

    [Fact]
    public async Task WithBodyTimeout_BodyIsBufferedBeforeReturning()
    {
        var handler = new SendTimeoutDelegatingHandler(TimeSpan.FromSeconds(30), bodyTimeout: TimeSpan.FromSeconds(30))
        {
            InnerHandler = new FixedBodyHandler("User-agent: *")
        };
        using var invoker = new HttpMessageInvoker(handler);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://ok.test/robots.txt"), CancellationToken.None);

        Assert.Equal("User-agent: *", await response.Content.ReadAsStringAsync(CancellationToken.None));
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StalledBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) });
    }

    private sealed class FixedBodyHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    /// <summary>A body whose first read never completes until cancelled.</summary>
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}

public class HttpRequestFailuresTests
{
    [Fact]
    public void ResiliencePipelineExceptions_CountAsRequestFailures()
    {
        // Regression test: per-request isolation (Ford's ~210 lookups, IFZ's model groups, downloads)
        // only caught HttpRequestException/TaskCanceledException, while the resilience pipeline throws
        // TimeoutRejectedException and BrokenCircuitException - one slow request failed the whole brand.
        Assert.True(HttpRequestFailures.IsRequestFailure(new TimeoutRejectedException(), CancellationToken.None));
        Assert.True(HttpRequestFailures.IsRequestFailure(new BrokenCircuitException(), CancellationToken.None));
        Assert.True(HttpRequestFailures.IsRequestFailure(new HttpRequestException(), CancellationToken.None));
        Assert.True(HttpRequestFailures.IsRequestFailure(new TaskCanceledException(), CancellationToken.None));
    }

    [Fact]
    public void CallerCancellation_IsNotARequestFailure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.False(HttpRequestFailures.IsRequestFailure(new TaskCanceledException(), cts.Token));
        Assert.False(HttpRequestFailures.IsRequestFailure(new InvalidOperationException(), CancellationToken.None));
    }

    [Fact]
    public async Task DownloadPdfAsync_ResilienceTimeout_IsAFailedDownload_NotAnException()
    {
        using var client = new HttpClient(new ThrowingHandler(new TimeoutRejectedException()));

        var result = await HttpDownloadHelper.DownloadPdfAsync(client, "https://slow.test/a.pdf", CancellationToken.None);

        Assert.False(result.Success);
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }
}
