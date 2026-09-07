using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Core.Orchestration;
using Rettungskarten.Infrastructure.Config;
using Rettungskarten.Infrastructure.Storage;

namespace Rettungskarten.Cli.Commands;

public static class FetchRescueCardsCommand
{
    public static Command Build(Option<bool> verboseOption)
    {
        var brandOption = new Option<string>("--brand")
        {
            Description = "Marke (vw, audi, skoda, seat, cupra, porsche, all)",
            DefaultValueFactory = _ => "all"
        };
        brandOption.AcceptOnlyFromAmong("vw", "audi", "skoda", "seat", "cupra", "porsche", "all");

        var outputOption = new Option<string>("--output")
        {
            Description = "Zielverzeichnis für Rettungskarten",
            DefaultValueFactory = _ => Path.Combine("data", "rescue-cards")
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Nur Discovery + Metadaten, keine PDF-Downloads"
        };

        var siblingConfigOption = new Option<string>("--sibling-config")
        {
            Description = "Pfad zur sibling-models.json (Standard: mitgelieferte Datei)",
            DefaultValueFactory = _ => ConfigLoader.DefaultSiblingModelsPath()
        };

        var command = new Command("rescue-cards", "Lädt Rettungskarten für Fahrzeugmarken der VW-Gruppe (nur Deutsch)");
        command.Add(brandOption);
        command.Add(outputOption);
        command.Add(dryRunOption);
        command.Add(siblingConfigOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var brandArg = parseResult.GetValue(brandOption)!;
            var output = parseResult.GetValue(outputOption)!;
            var dryRun = parseResult.GetValue(dryRunOption);
            var siblingConfigPath = parseResult.GetValue(siblingConfigOption)!;
            var verbose = parseResult.GetValue(verboseOption);

            using var services = CompositionRoot.Build(verbose);
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("RescueCards");

            var brands = brandArg == "all" ? Enum.GetValues<Brand>() : [ParseBrand(brandArg)];

            var siblingConfig = await ConfigLoader.LoadSiblingModelsAsync(siblingConfigPath, ct);
            var sources = services.GetServices<IRescueCardSource>();
            var store = new FileSystemRescueCardStore(new RescueCardStoreOptions { RootPath = output });
            var orchestrator = new RescueCardOrchestrator(
                sources, store, siblingConfig, services.GetRequiredService<ILogger<RescueCardOrchestrator>>());

            var results = new List<BrandRunResult>();
            foreach (var brand in brands)
            {
                logger.LogInformation("Starte {Brand}...", brand);
                results.Add(await orchestrator.RunForBrandAsync(brand, dryRun, ct));
            }

            RunSummaryPrinter.Print(results);
            return RunSummaryPrinter.HasUnexpectedFailures(results) ? 1 : 0;
        });

        return command;
    }

    private static Brand ParseBrand(string value) => value.ToLowerInvariant() switch
    {
        "vw" => Brand.VW,
        "audi" => Brand.Audi,
        "skoda" => Brand.Skoda,
        "seat" => Brand.Seat,
        "cupra" => Brand.Cupra,
        "porsche" => Brand.Porsche,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unbekannte Marke")
    };
}
