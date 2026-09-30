using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Infrastructure.Http;

namespace Rettungskarten.Tests.Http;

public class RobotsTxtRulesTests
{
    // Trimmed from the real fiat.de robots.txt (served only to the browser-header client).
    private const string FiatRobotsTxt = """
        User-agent: *

        Disallow: /content/dam/fiat/formpdf
        Disallow: *.pdf$
        Disallow: */tabs-container
        Disallow: /*?*sort=

        Sitemap: https://www.fiat.de/sitemap.xml
        """;

    [Theory]
    [InlineData("/content/dam/fiat/de/rettungskarten/500.pdf", false)]
    [InlineData("/content/dam/fiat/de/rettungskarten/500.PDF", true)] // matching is case-sensitive per RFC 9309
    [InlineData("/content/dam/fiat/de/rettungskarten/500.pdf?x=1", true)] // "$" anchors at the very end
    [InlineData("/rettungsdatenblaetter", true)]
    [InlineData("/modelle/tabs-container", false)]
    [InlineData("/modelle?a=1&sort=price", false)]
    public void IsAllowed_FiatRules(string pathAndQuery, bool expected)
    {
        var rules = RobotsTxtRules.Parse(FiatRobotsTxt, PolitenessOptions.ProductToken);

        Assert.Equal(expected, rules.IsAllowed(pathAndQuery));
    }

    [Fact]
    public void IsAllowed_LongestMatchWins_AndAllowWinsTies()
    {
        var rules = RobotsTxtRules.Parse("""
            User-agent: *
            Disallow: /docs/
            Allow: /docs/rescue/
            Disallow: /same
            Allow: /same
            """, PolitenessOptions.ProductToken);

        Assert.False(rules.IsAllowed("/docs/manual.pdf"));
        Assert.True(rules.IsAllowed("/docs/rescue/card.pdf"));
        Assert.True(rules.IsAllowed("/same"));
    }

    [Fact]
    public void Parse_GroupNamingThisTool_ReplacesTheWildcardGroup()
    {
        var rules = RobotsTxtRules.Parse("""
            User-agent: *
            Disallow: /

            User-agent: RettungskartenTool
            Disallow: /private/
            """, PolitenessOptions.ProductToken);

        Assert.True(rules.IsAllowed("/public/card.pdf"));
        Assert.False(rules.IsAllowed("/private/card.pdf"));
    }

    [Fact]
    public void Parse_ConsecutiveUserAgentLines_ShareOneGroup_AndEmptyDisallowAllowsEverything()
    {
        var rules = RobotsTxtRules.Parse("""
            User-agent: googlebot
            User-agent: *
            Disallow:
            """, PolitenessOptions.ProductToken);

        Assert.True(rules.IsAllowed("/anything.pdf"));
    }

    [Fact]
    public void Parse_OtherBotsRules_DoNotApply()
    {
        var rules = RobotsTxtRules.Parse("""
            User-agent: GPTBot
            Disallow: /
            """, PolitenessOptions.ProductToken);

        Assert.True(rules.IsAllowed("/card.pdf"));
    }
}

public class RobotsTxtDelegatingHandlerTests
{
    [Fact]
    public async Task DisallowedUrl_NeverReachesTheNetwork_AndIsMarkedBlocked()
    {
        var inner = new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/robots.txt"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("User-agent: *\nDisallow: *.pdf$") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        using var client = CreateClient(inner);

        using var blocked = await client.GetAsync("https://example.test/card.pdf");
        using var allowed = await client.GetAsync("https://example.test/page.html");

        Assert.Equal(451, (int)blocked.StatusCode);
        Assert.True(RobotsTxtDelegatingHandler.IsBlockedResponse(blocked));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.DoesNotContain(inner.Paths, p => p == "/card.pdf");
        Assert.Single(inner.Paths, p => p == "/robots.txt"); // fetched once, then cached
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task UnreadableRobotsTxt_AllowsTheRequest(HttpStatusCode robotsStatus)
    {
        var inner = new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/robots.txt"
            ? new HttpResponseMessage(robotsStatus)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        using var client = CreateClient(inner);

        using var response = await client.GetAsync("https://example.test/card.pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RulesAreCachedPerUserAgent_NotJustPerHost()
    {
        // Regression test for a bug found live against fiat.de: its Akamai front answers robots.txt
        // with 403 to the plain client (= no restrictions) but serves the real "Disallow: *.pdf$" to the
        // browser-header client. With one cache entry per host, whichever client asked first decided
        // for both - the browser client then downloaded PDFs robots.txt forbids.
        var inner = new RecordingHandler(request =>
        {
            var isBrowser = request.Headers.UserAgent.ToString().Contains("Mozilla");
            if (request.RequestUri!.AbsolutePath == "/robots.txt")
            {
                return isBrowser
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("User-agent: *\nDisallow: *.pdf$") }
                    : new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });
        var policy = new RobotsTxtPolicy(NullLogger<RobotsTxtPolicy>.Instance);
        using var plainClient = CreateClient(inner, policy, "RettungskartenTool/1.0");
        using var browserClient = CreateClient(inner, policy, "Mozilla/5.0 Chrome/140.0");

        using var plain = await plainClient.GetAsync("https://example.test/card.pdf");
        using var browser = await browserClient.GetAsync("https://example.test/card.pdf");

        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        Assert.True(RobotsTxtDelegatingHandler.IsBlockedResponse(browser));
    }

    private static HttpClient CreateClient(RecordingHandler inner, RobotsTxtPolicy? policy = null, string userAgent = "RettungskartenTool/1.0")
    {
        policy ??= new RobotsTxtPolicy(NullLogger<RobotsTxtPolicy>.Instance);
        var client = new HttpClient(new RobotsTxtDelegatingHandler(policy) { InnerHandler = inner });
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Paths)
            {
                Paths.Add(request.RequestUri!.AbsolutePath);
            }

            return Task.FromResult(respond(request));
        }
    }
}
