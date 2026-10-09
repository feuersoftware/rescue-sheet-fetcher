using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Rettungskarten.Infrastructure.Http;

/// <summary>Timeouts of the named clients - see <see cref="HttpServiceCollectionExtensions"/>.
/// <paramref name="Send"/> bounds one attempt from the moment it is actually sent (after the rate-limit
/// slot), <paramref name="Total"/> one logical request including its retries and every wait.</summary>
internal sealed record HttpClientTimeouts(TimeSpan Send, TimeSpan Total, TimeSpan LongRunningSend, TimeSpan LongRunningTotal)
{
    public static readonly HttpClientTimeouts Default = new(
        Send: TimeSpan.FromSeconds(10),
        Total: TimeSpan.FromSeconds(60),
        LongRunningSend: TimeSpan.FromMinutes(2),
        LongRunningTotal: TimeSpan.FromMinutes(6));
}

public static class HttpServiceCollectionExtensions
{
    public static IServiceCollection AddRettungskartenHttpClient(
        this IServiceCollection services, PolitenessOptions? options = null) =>
        services.AddRettungskartenHttpClient(options, HttpClientTimeouts.Default);

    /// <summary>
    /// Handler order of every named client, outermost first:
    ///
    /// 1. the standard resilience handler (total timeout, retry, per-host circuit breaker);
    /// 2. <see cref="RedirectDelegatingHandler"/> - follows redirects, so each hop passes 3-5 again (the
    ///    primary handlers have automatic redirects off; they would follow them below everything else);
    /// 3. <see cref="RobotsTxtDelegatingHandler"/> - a refused request never reaches the network and,
    ///    being inside the resilience handler, is never retried;
    /// 4. <see cref="PoliteDelegatingHandler"/> - the per-host rate limiter, innermost of the policy
    ///    handlers so every retry attempt (not just the first) waits for a slot, which matters most for
    ///    exactly the flaky/rate-limited hosts the retries exist for;
    /// 5. <see cref="SendTimeoutDelegatingHandler"/> - the per-attempt timeout, started only once the
    ///    slot is acquired, so queueing behind other brands' requests to a shared host (or KBA's 30 s
    ///    crawl-delay) is never mistaken for a slow server. It also reads the response body into memory
    ///    within the total budget, since nothing above it bounds the body (see its doc comment).
    ///
    /// The resilience handler's own attempt timeout would wrap 2-5 including the queue, so it is set to
    /// the total budget, where it never fires first (Polly can't switch it off). AddStandardResilienceHandler
    /// also sets HttpClient.Timeout to infinite and governs the real limit through TotalRequestTimeout - a
    /// `client.Timeout = ...` would silently be overwritten (verified empirically).
    ///
    /// SelectPipelineByAuthority gives every host its own resilience pipeline - and with it its own
    /// circuit breaker. With one shared breaker per client, a single manufacturer site timing out
    /// repeatedly would open the circuit for every other brand's requests running concurrently on the
    /// same client, which with ~60 brands running in parallel is no longer hypothetical.
    /// </summary>
    internal static IServiceCollection AddRettungskartenHttpClient(
        this IServiceCollection services, PolitenessOptions? options, HttpClientTimeouts timeouts)
    {
        options ??= new PolitenessOptions();
        services.AddSingleton(options);
        services.AddSingleton<HostRateLimiter>();
        services.AddSingleton<RobotsTxtPolicy>();
        services.AddSingleton<DiscoveryResponseCache>();
        services.AddTransient<RedirectDelegatingHandler>();
        services.AddTransient<RobotsTxtDelegatingHandler>();
        services.AddTransient<PoliteDelegatingHandler>();

        var defaultClient = services
            .AddHttpClient(RettungskartenHttpClient.Name, client => client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        AddHandlers(defaultClient, timeouts.Send, timeouts.Total);

        // Separate, longer-timeout client for multi-MB downloads (Porsche's ~55MB combined PDF, Subaru's
        // ~41MB one, BMW's up to ~20MB high-voltage sheets) - scoped to this client rather than widening
        // the default client's timeouts for every brand, since the short default send timeout is
        // deliberately tuned for the small per-model-page requests every other brand source makes and
        // gives them a fast fail-fast/circuit-break safety net that a blanket multi-minute timeout would
        // blunt.
        var largeDownloadClient = services
            .AddHttpClient(RettungskartenHttpClient.LargeDownloadName, client => client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        AddHandlers(largeDownloadClient, timeouts.LongRunningSend, timeouts.LongRunningTotal);

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
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false
        });
        AddHandlers(browserClient, timeouts.LongRunningSend, timeouts.LongRunningTotal);

        return services;
    }

    private static void AddHandlers(IHttpClientBuilder builder, TimeSpan sendTimeout, TimeSpan totalTimeout)
    {
        // Two statements rather than one fluent chain: AddStandardResilienceHandler returns a
        // resilience-pipeline builder, not IHttpClientBuilder. The first-added handler is outermost.
        builder.AddStandardResilienceHandler(resilience =>
        {
            resilience.TotalRequestTimeout.Timeout = totalTimeout;
            resilience.AttemptTimeout.Timeout = totalTimeout;
            // Polly requires the breaker's sampling window to be at least twice the attempt timeout.
            resilience.CircuitBreaker.SamplingDuration = totalTimeout * 2;
        }).SelectPipelineByAuthority();

        builder.AddHttpMessageHandler<RedirectDelegatingHandler>();
        builder.AddHttpMessageHandler<RobotsTxtDelegatingHandler>();
        builder.AddHttpMessageHandler<PoliteDelegatingHandler>();
        builder.AddHttpMessageHandler(() => new SendTimeoutDelegatingHandler(sendTimeout, bodyTimeout: totalTimeout));
    }
}

public static class RettungskartenHttpClient
{
    public const string Name = "rettungskarten";

    /// <summary>Use only for downloads expected to run into multiple MB/minutes (Porsche's and
    /// Subaru's combined PDFs, BMW's and Hyundai's large sheets) - see
    /// <see cref="HttpServiceCollectionExtensions.AddRettungskartenHttpClient(IServiceCollection, PolitenessOptions?)"/>.</summary>
    public const string LargeDownloadName = "rettungskarten-large-download";

    /// <summary>Use only for hosts that reject non-browser requests (Akamai bot protection) - see
    /// <see cref="HttpServiceCollectionExtensions.AddRettungskartenHttpClient(IServiceCollection, PolitenessOptions?)"/>.</summary>
    public const string BrowserName = "rettungskarten-browser";
}
