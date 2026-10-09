using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Rettungskarten.Infrastructure.Http;

namespace Rettungskarten.Tests.Http;

public class HttpServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(RettungskartenHttpClient.Name)]
    [InlineData(RettungskartenHttpClient.LargeDownloadName)]
    [InlineData(RettungskartenHttpClient.BrowserName)]
    public void AddRettungskartenHttpClient_HandlerOrder(string clientName)
    {
        // Outermost first: resilience, redirects, robots.txt check, rate limiter, send timeout. The
        // first-added handler in HttpMessageHandlerBuilder.AdditionalHandlers ends up outermost.
        // - The rate limiter must be inside the resilience handler, so Polly's retries re-invoke it on
        //   every attempt instead of bypassing it after the first.
        // - The send timeout must be inside the rate limiter, so time queued for a host's slot never
        //   counts as a slow server.
        // - The robots.txt check must be inside the redirect handler, so every redirect hop is checked.
        var services = new ServiceCollection();
        services.AddRettungskartenHttpClient();

        IReadOnlyList<DelegatingHandler>? capturedHandlers = null;
        HttpMessageHandler? capturedPrimary = null;
        services.Configure<HttpClientFactoryOptions>(clientName, o => o.HttpMessageHandlerBuilderActions.Add(builder =>
        {
            capturedHandlers = builder.AdditionalHandlers.ToList();
            capturedPrimary = builder.PrimaryHandler;
        }));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        Assert.NotNull(capturedHandlers);
        Assert.Equal(5, capturedHandlers.Count);
        Assert.IsType<RedirectDelegatingHandler>(capturedHandlers[1]);
        Assert.IsType<RobotsTxtDelegatingHandler>(capturedHandlers[2]);
        Assert.IsType<PoliteDelegatingHandler>(capturedHandlers[3]);
        Assert.IsType<SendTimeoutDelegatingHandler>(capturedHandlers[4]);

        // Redirects are followed by RedirectDelegatingHandler; the primary handler would follow them
        // below the robots.txt check and the rate limiter.
        var allowsAutoRedirect = capturedPrimary switch
        {
            HttpClientHandler h => h.AllowAutoRedirect,
            SocketsHttpHandler s => s.AllowAutoRedirect,
            _ => throw new InvalidOperationException($"Unexpected primary handler {capturedPrimary?.GetType()}")
        };
        Assert.False(allowsAutoRedirect);
    }

    [Fact]
    public async Task RequestsQueuedForAHostsRateLimitSlot_DoNotTimeOut()
    {
        // Regression test: with the resilience pipeline's attempt timeout wrapping the rate limiter, a
        // request waiting behind other requests to the same host (several brands sharing a portal,
        // KBA's crawl-delay) "timed out" while still queued and was retried. Scaled down: a 1 s send
        // timeout and a 0.7 s per-host delay, so the third of three concurrent requests waits ~1.4 s.
        var logLines = new ConcurrentQueue<string>();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(new CapturingLoggerProvider(logLines)));
        services.AddRettungskartenHttpClient(
            new PolitenessOptions { DefaultDelay = TimeSpan.FromMilliseconds(700) },
            new HttpClientTimeouts(
                Send: TimeSpan.FromSeconds(1), Total: TimeSpan.FromSeconds(20),
                LongRunningSend: TimeSpan.FromSeconds(1), LongRunningTotal: TimeSpan.FromSeconds(20)));
        var primary = new CountingOkHandler();
        services.AddHttpClient(RettungskartenHttpClient.Name).ConfigurePrimaryHttpMessageHandler(() => primary);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var statuses = await Task.WhenAll(Enumerable.Range(1, 3).Select(async i =>
        {
            using var response = await factory.CreateClient(RettungskartenHttpClient.Name).GetAsync($"https://shared-host.test/doc{i}.pdf");
            return response.StatusCode;
        }));

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.DoesNotContain(logLines, l => l.Contains("OnTimeout", StringComparison.Ordinal) || l.Contains("OnRetry", StringComparison.Ordinal));
        Assert.Equal(4, primary.Count); // robots.txt + three documents, no retried attempts
    }

    [Fact]
    public async Task SendTimeout_IsRetriedByTheResiliencePipeline()
    {
        // The per-attempt timeout moved out of Polly into SendTimeoutDelegatingHandler; it must still
        // count as a transient failure the standard retry handles, or a slow response would no longer
        // be retried at all.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRettungskartenHttpClient(
            new PolitenessOptions { DefaultDelay = TimeSpan.Zero },
            new HttpClientTimeouts(
                Send: TimeSpan.FromMilliseconds(300), Total: TimeSpan.FromSeconds(20),
                LongRunningSend: TimeSpan.FromMilliseconds(300), LongRunningTotal: TimeSpan.FromSeconds(20)));
        var primary = new FirstAttemptHangsHandler();
        services.AddHttpClient(RettungskartenHttpClient.Name).ConfigurePrimaryHttpMessageHandler(() => primary);

        using var provider = services.BuildServiceProvider();
        using var response = await provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(RettungskartenHttpClient.Name).GetAsync("https://slow.test/a.pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, primary.Attempts);
    }

    private sealed class FirstAttemptHangsHandler : HttpMessageHandler
    {
        private int _attempts;

        public int Attempts => _attempts;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/robots.txt")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (Interlocked.Increment(ref _attempts) == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken); // slower than the send timeout
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task RedirectHops_AreCheckedAgainstTheTargetHostsRobotsTxt()
    {
        // A redirect used to be followed inside the primary handler, below the robots.txt check - an
        // allowed URL that redirects to a disallowed one got the disallowed file anyway.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRettungskartenHttpClient(new PolitenessOptions { DefaultDelay = TimeSpan.Zero });
        var primary = new CountingOkHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            "https://portal.test/robots.txt" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "https://cdn.test/robots.txt" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("User-agent: *\nDisallow: /private/") },
            "https://portal.test/d/123" => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://cdn.test/private/sheet.pdf") } },
            _ => new HttpResponseMessage(HttpStatusCode.OK)
        });
        services.AddHttpClient(RettungskartenHttpClient.Name).ConfigurePrimaryHttpMessageHandler(() => primary);

        using var provider = services.BuildServiceProvider();
        using var response = await provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(RettungskartenHttpClient.Name).GetAsync("https://portal.test/d/123");

        Assert.True(RobotsTxtDelegatingHandler.IsBlockedResponse(response));
        Assert.DoesNotContain(primary.Urls, u => u == "https://cdn.test/private/sheet.pdf");
        Assert.Equal("https://cdn.test/private/sheet.pdf", response.RequestMessage?.RequestUri?.AbsoluteUri);
    }

    private sealed class CountingOkHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond = null) : HttpMessageHandler
    {
        private int _count;

        public int Count => _count;

        public ConcurrentQueue<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            Urls.Enqueue(request.RequestUri!.AbsoluteUri);
            var response = respond?.Invoke(request) ?? new HttpResponseMessage(request.RequestUri!.AbsolutePath == "/robots.txt"
                ? HttpStatusCode.NotFound
                : HttpStatusCode.OK);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(lines);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue(formatter(state, exception));
        }
    }
}
