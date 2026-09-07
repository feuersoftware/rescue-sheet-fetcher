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
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            .AddHttpMessageHandler<PoliteDelegatingHandler>()
            .AddStandardResilienceHandler();

        return services;
    }
}

public static class RettungskartenHttpClient
{
    public const string Name = "rettungskarten";
}
