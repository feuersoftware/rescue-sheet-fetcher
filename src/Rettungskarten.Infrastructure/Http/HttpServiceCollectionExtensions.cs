using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Rettungskarten.Infrastructure.Http;

public static class HttpServiceCollectionExtensions
{
    public static IServiceCollection AddRettungskartenHttpClient(
        this IServiceCollection services, PolitenessOptions? options = null)
    {
        options ??= new PolitenessOptions();
        services.AddSingleton(options);
        services.AddSingleton<HostRateLimiter>();
        services.AddSingleton<RobotsTxtPolicy>();
        services.AddSingleton<DiscoveryResponseCache>();
        services.AddTransient<PoliteDelegatingHandler>();
        services.AddTransient<RobotsTxtDelegatingHandler>();

        var defaultClient = services.AddHttpClient(RettungskartenHttpClient.Name, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        });

        // AddStandardResilienceHandler sets HttpClient.Timeout to Timeout.InfiniteTimeSpan itself and
        // governs the real timeout entirely through its own TotalRequestTimeout policy - a
        // `client.Timeout = ...` line here would silently be overwritten and do nothing (verified
        // empirically), and the *default* TotalRequestTimeout (30s) is shorter than intended, so it
        // must be set explicitly here rather than relying on the parameterless overload's default.
        //
        // Registered BEFORE (= outside) PoliteDelegatingHandler on purpose, in a separate statement
        // rather than fluently chained off this call's own return value (AddStandardResilienceHandler
        // returns a resilience-pipeline builder, not IHttpClientBuilder, so it can't be chained
        // further): the first-added handler ends up outermost, so a handler added after this one only
        // sees each *external* call once, while Polly's internal retries re-invoke whatever is next
        // inside it directly, never bubbling back out through an outer handler. Adding
        // PoliteDelegatingHandler afterwards instead makes it innermost, so every retry attempt - not
        // just the first - passes through the per-host rate limiter, which matters most for exactly the
        // flaky/rate-limited hosts these retries exist for. RobotsTxtDelegatingHandler sits between
        // the two (see its doc comment).
        //
        // SelectPipelineByAuthority gives every host its own resilience pipeline - and with it its own
        // circuit breaker. With one shared breaker per client, a single manufacturer site timing out
        // repeatedly would open the circuit for every other brand's requests running concurrently on
        // the same client, which with ~60 brands running in parallel is no longer hypothetical.
        defaultClient.AddStandardResilienceHandler(resilience =>
        {
            resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
        }).SelectPipelineByAuthority();
        AddInnerHandlers(defaultClient);

        // Separate, longer-timeout client for Porsche's combined rescue-data PDF (a single ~55MB file
        // covering every model - see PorscheRescueCardSource) - scoped to this one client rather than
        // widening the shared client's timeouts for every brand, since the default 60s/short-attempt
        // settings above are deliberately tuned for the small per-model-page requests every other
        // brand source makes and give them a fast fail-fast/circuit-break safety net that a blanket
        // multi-minute timeout would blunt.
        var largeDownloadClient = services.AddHttpClient(RettungskartenHttpClient.LargeDownloadName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            // No client.Timeout here - see the comment on the default client above, it would be
            // silently overwritten by AddStandardResilienceHandler; TotalRequestTimeout below is what
            // actually governs this.
        });

        // See the comment on the default client's registration above - outermost/innermost order and
        // the reason it's two statements instead of one fluent chain both apply here too.
        largeDownloadClient.AddStandardResilienceHandler(ConfigureLongRunning).SelectPipelineByAuthority();
        AddInnerHandlers(largeDownloadClient);

        // Browser-like client for Akamai-fronted hosts (Stellantis brand sites, Volvo, Tesla, Ford's
        // content CDN): they answer 403 to anything that doesn't send the header set a real browser
        // navigation sends. Verified by elimination against the live sites - the User-Agent alone is
        // not enough, the Sec-Fetch-* trio and Accept-Language are also required, and without
        // automatic decompression the (always compressed) response body is unreadable. Long-running
        // timeouts like the large-download client, since Ford's all-models PDF (~14MB) also comes
        // through here. Every robots.txt rule still applies (RobotsTxtDelegatingHandler matches this
        // tool's product token, not this User-Agent - see PolitenessOptions.ProductToken).
        var browserClient = services.AddHttpClient(RettungskartenHttpClient.BrowserName, client =>
        {
            var headers = client.DefaultRequestHeaders;
            headers.UserAgent.ParseAdd(options.BrowserUserAgent);
            headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,application/pdf,*/*;q=0.8");
            headers.AcceptLanguage.ParseAdd("de-DE,de;q=0.9,en;q=0.8");
            headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
            headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
            headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        });

        browserClient.AddStandardResilienceHandler(ConfigureLongRunning).SelectPipelineByAuthority();
        AddInnerHandlers(browserClient);

        return services;
    }

    private static void ConfigureLongRunning(HttpStandardResilienceOptions resilience)
    {
        resilience.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
        resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
        resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
    }

    private static void AddInnerHandlers(IHttpClientBuilder builder)
    {
        builder.AddHttpMessageHandler<RobotsTxtDelegatingHandler>();
        builder.AddHttpMessageHandler<PoliteDelegatingHandler>();
    }
}

public static class RettungskartenHttpClient
{
    public const string Name = "rettungskarten";

    /// <summary>Use only for downloads expected to run into multiple MB/minutes (currently just
    /// Porsche's combined PDF) - see the registration comment in <see cref="HttpServiceCollectionExtensions.AddRettungskartenHttpClient"/>.</summary>
    public const string LargeDownloadName = "rettungskarten-large-download";

    /// <summary>Use only for hosts that reject non-browser requests (Akamai bot protection) - see the
    /// registration comment in <see cref="HttpServiceCollectionExtensions.AddRettungskartenHttpClient"/>.</summary>
    public const string BrowserName = "rettungskarten-browser";
}
