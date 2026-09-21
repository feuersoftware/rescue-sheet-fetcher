using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Rettungskarten.Infrastructure.Http;

namespace Rettungskarten.Tests.Http;

public class HttpServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(RettungskartenHttpClient.Name)]
    [InlineData(RettungskartenHttpClient.LargeDownloadName)]
    public void AddRettungskartenHttpClient_PoliteDelegatingHandlerIsInnermost(string clientName)
    {
        // Regression test: PoliteDelegatingHandler must be registered AFTER (= closer to the transport
        // than) the resilience handler, so Polly's internal retries re-invoke it on every attempt
        // instead of bypassing the rate limiter after the first. The first-added handler in
        // HttpMessageHandlerBuilder.AdditionalHandlers ends up outermost, so "innermost" here means
        // "last in the list".
        var services = new ServiceCollection();
        services.AddRettungskartenHttpClient();

        IReadOnlyList<DelegatingHandler>? capturedHandlers = null;
        services.Configure<HttpClientFactoryOptions>(clientName, o =>
            o.HttpMessageHandlerBuilderActions.Add(builder => capturedHandlers = builder.AdditionalHandlers.ToList()));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        Assert.NotNull(capturedHandlers);
        Assert.Equal(2, capturedHandlers.Count);
        Assert.IsType<PoliteDelegatingHandler>(capturedHandlers[^1]);
        Assert.IsNotType<PoliteDelegatingHandler>(capturedHandlers[0]);
    }
}
