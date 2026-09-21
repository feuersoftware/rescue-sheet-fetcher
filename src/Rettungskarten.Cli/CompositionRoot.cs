using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Infrastructure.Stock;

namespace Rettungskarten.Cli;

/// <summary>
/// Wires up the pieces that don't vary per command invocation (HTTP client, logging, brand sources).
/// Path-dependent objects (stores, the orchestrator) are constructed per-command from CLI options
/// instead of through the container, since those paths aren't known until the command runs.
/// </summary>
public static class CompositionRoot
{
    public static ServiceProvider Build(bool verbose)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });

        services.AddRettungskartenHttpClient();

        services.AddTransient<IRescueCardSource, VwRescueCardSource>();
        services.AddTransient<IRescueCardSource, AudiRescueCardSource>();
        services.AddTransient<IRescueCardSource, SkodaRescueCardSource>();
        services.AddTransient<IRescueCardSource, SeatRescueCardSource>();
        services.AddTransient<IRescueCardSource, CupraRescueCardSource>();
        services.AddTransient<IRescueCardSource, PorscheRescueCardSource>();
        services.AddTransient<IRescueCardSource, BentleyRescueCardSource>();
        services.AddTransient<IRescueCardSource, LamborghiniRescueCardSource>();

        services.AddTransient<KbaVehicleStockSource>();

        return services.BuildServiceProvider();
    }
}
