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
        services.AddTransient<PoliteDelegatingHandler>();

        services.AddHttpClient(RettungskartenHttpClient.Name, client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            })
            .AddHttpMessageHandler<PoliteDelegatingHandler>()
            // AddStandardResilienceHandler sets HttpClient.Timeout to Timeout.InfiniteTimeSpan itself
            // and governs the real timeout entirely through its own TotalRequestTimeout policy - a
            // `client.Timeout = ...` line here would silently be overwritten and do nothing (verified
            // empirically), and the *default* TotalRequestTimeout (30s) is shorter than intended, so it
            // must be set explicitly here rather than relying on the parameterless overload's default.
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
            });

        // Separate, longer-timeout client for Porsche's combined rescue-data PDF (a single ~55MB file
        // covering every model - see PorscheRescueCardSource) - scoped to this one client rather than
        // widening the shared client's timeouts for every brand, since the default 60s/short-attempt
        // settings above are deliberately tuned for the small per-model-page requests every other
        // brand source makes and give them a fast fail-fast/circuit-break safety net that a blanket
        // multi-minute timeout would blunt.
        services.AddHttpClient(RettungskartenHttpClient.LargeDownloadName, client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                // No client.Timeout here - see the comment on the default client above, it would be
                // silently overwritten by AddStandardResilienceHandler; TotalRequestTimeout below is
                // what actually governs this.
            })
            .AddHttpMessageHandler<PoliteDelegatingHandler>()
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
            });

        return services;
    }
}

public static class RettungskartenHttpClient
{
    public const string Name = "rettungskarten";

    /// <summary>Use only for downloads expected to run into multiple MB/minutes (currently just
    /// Porsche's combined PDF) - see the registration comment in <see cref="AddRettungskartenHttpClient"/>.</summary>
    public const string LargeDownloadName = "rettungskarten-large-download";
}
