using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Infrastructure.RescueCards.BmwGroup;
using Rettungskarten.Infrastructure.RescueCards.IfzBerlin;
using Rettungskarten.Infrastructure.RescueCards.Mercedes;
using Rettungskarten.Infrastructure.RescueCards.RenaultGroup;
using Rettungskarten.Infrastructure.RescueCards.Smart;
using Rettungskarten.Infrastructure.RescueCards.Stellantis;
using Rettungskarten.Infrastructure.RescueCards.ToyotaGroup;
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

        // Portal sources serving several brands: one instance per brand (see each class's doc comment).
        foreach (var brand in new[] { Brand.BMW, Brand.Mini, Brand.RollsRoyce })
        {
            services.AddTransient<IRescueCardSource>(sp => new BmwGroupRescueCardSource(
                brand, sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<ILogger<BmwGroupRescueCardSource>>()));
        }

        // rk.mb-qr.com serves Mercedes-Benz, AMG, EQ, Maybach and pre-2022 smart; smart's post-2022
        // models come from its own site as a second smart source.
        foreach (var brand in new[] { Brand.MercedesBenz, Brand.MercedesAmg, Brand.MercedesEq, Brand.Maybach, Brand.Smart })
        {
            services.AddTransient<IRescueCardSource>(sp => ActivatorUtilities.CreateInstance<MercedesRescueCardSource>(sp, brand));
        }

        services.AddTransient<IRescueCardSource, SmartRescueCardSource>();

        foreach (var brand in new[] { Brand.Renault, Brand.Dacia })
        {
            services.AddTransient<IRescueCardSource>(sp => ActivatorUtilities.CreateInstance<RenaultGroupRescueCardSource>(sp, brand));
        }

        foreach (var brand in new[] { Brand.Toyota, Brand.Lexus })
        {
            services.AddTransient<IRescueCardSource>(sp => ActivatorUtilities.CreateInstance<ToyotaGroupRescueCardSource>(sp, brand));
        }

        services.AddTransient<IRescueCardSource, DaihatsuRescueCardSource>();

        foreach (var brand in new[] { Brand.Peugeot, Brand.Citroen, Brand.DS })
        {
            services.AddTransient<IRescueCardSource>(sp => ActivatorUtilities.CreateInstance<ServiceboxRescueCardSource>(sp, brand));
        }

        foreach (var brand in new[] { Brand.Opel, Brand.Vauxhall, Brand.Saab, Brand.Chevrolet, Brand.Cadillac })
        {
            services.AddTransient<IRescueCardSource>(sp => ActivatorUtilities.CreateInstance<IfzBerlinRescueCardSource>(sp, brand));
        }

        foreach (var brand in StellantisBrandSiteRescueCardSource.SupportedBrands)
        {
            services.AddTransient<IRescueCardSource>(sp => new StellantisBrandSiteRescueCardSource(
                brand, sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<ILogger<StellantisBrandSiteRescueCardSource>>()));
        }

        services.AddTransient<IRescueCardSource, MaseratiRescueCardSource>();

        services.AddTransient<IRescueCardSource, HyundaiRescueCardSource>();
        services.AddTransient<IRescueCardSource, KiaRescueCardSource>();
        services.AddTransient<IRescueCardSource, NissanRescueCardSource>();
        services.AddTransient<IRescueCardSource, FordRescueCardSource>();
        services.AddTransient<IRescueCardSource, MazdaRescueCardSource>();
        services.AddTransient<IRescueCardSource, HondaRescueCardSource>();
        services.AddTransient<IRescueCardSource, VolvoRescueCardSource>();
        services.AddTransient<IRescueCardSource, JaguarRescueCardSource>();
        services.AddTransient<IRescueCardSource, LandRoverRescueCardSource>();
        services.AddTransient<IRescueCardSource, TeslaRescueCardSource>();
        services.AddTransient<IRescueCardSource, MgRescueCardSource>();
        services.AddTransient<IRescueCardSource, KgmRescueCardSource>();

        services.AddTransient<IRescueCardSource, MitsubishiRescueCardSource>();
        services.AddTransient<IRescueCardSource, SubaruRescueCardSource>();
        services.AddTransient<IRescueCardSource, SuzukiRescueCardSource>();
        services.AddTransient<IRescueCardSource, BydRescueCardSource>();
        services.AddTransient<IRescueCardSource, IsuzuRescueCardSource>();
        services.AddTransient<IRescueCardSource, MaxusRescueCardSource>();
        services.AddTransient<IRescueCardSource, RufRescueCardSource>();
        services.AddTransient<IRescueCardSource, StreetscooterRescueCardSource>();
        services.AddTransient<IRescueCardSource, PolestarRescueCardSource>();

        services.AddTransient<KbaVehicleStockSource>();

        return services.BuildServiceProvider();
    }
}
