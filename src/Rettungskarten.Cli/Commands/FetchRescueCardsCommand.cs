using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Localization;
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
            Description = Strings.Get("Option_Brand_Description"),
            DefaultValueFactory = _ => "all"
        };
        brandOption.AcceptOnlyFromAmong(BrandArgument.AllowedValues());

        // For the weekly link check: some manufacturer sites answer every request from GitHub-hosted
        // runners with 403 (see link-check.yml for why), so CI skips them explicitly instead of
        // failing every week on something no code change can fix.
        var excludeBrandsOption = new Option<string[]>("--exclude-brands")
        {
            Description = Strings.Get("Option_ExcludeBrands_Description"),
            AllowMultipleArgumentsPerToken = true
        };
        excludeBrandsOption.AcceptOnlyFromAmong(BrandArgument.AllowedValues().Where(v => v != BrandArgument.All).ToArray());

        var outputOption = new Option<string>("--output")
        {
            Description = Strings.Get("Option_Output_RescueCards_Description"),
            DefaultValueFactory = _ => Path.Combine("data", "rescue-cards")
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = Strings.Get("Option_DryRun_Description")
        };

        var siblingConfigOption = new Option<string>("--sibling-config")
        {
            Description = Strings.Get("Option_SiblingConfig_Description"),
            DefaultValueFactory = _ => ConfigLoader.DefaultSiblingModelsPath()
        };

        var command = new Command("rescue-cards", Strings.Get("Command_RescueCards_Description"));
        command.Add(brandOption);
        command.Add(excludeBrandsOption);
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

            var excluded = (parseResult.GetValue(excludeBrandsOption) ?? []).Select(BrandArgument.Parse).ToHashSet();
            var brands = BrandArgument.Resolve(brandArg).Where(b => !excluded.Contains(b)).ToList();
            if (excluded.Count > 0)
            {
                logger.LogInformation("{Message}", Strings.Get("Log_BrandsExcluded", string.Join(", ", excluded.Order())));
            }

            var siblingConfig = await ConfigLoader.LoadSiblingModelsAsync(siblingConfigPath, ct);
            var sources = services.GetServices<IRescueCardSource>();
            var store = new FileSystemRescueCardStore(new RescueCardStoreOptions { RootPath = output });
            var orchestrator = new RescueCardOrchestrator(
                sources, store, siblingConfig, services.GetRequiredService<ILogger<RescueCardOrchestrator>>());

            // HostRateLimiter rate-limits per host independently (see its own doc comment) - running
            // brands sequentially only paid the sum of every brand's discovery+download time for no
            // politeness benefit. Brands that share a host (portal sources serving several brands,
            // e.g. Mercedes/AMG/EQ/Maybach/smart or Peugeot/Citroen/DS) still queue behind each other
            // on that host's limiter, so parallelism never increases the request rate to any one site.
            // Task.WhenAll preserves the input order in its result array regardless of completion order,
            // so RunSummaryPrinter's table still prints in the same deterministic brand order as before,
            // not interleaved by whichever brand finishes first.
            var results = await Task.WhenAll(brands.Select(async brand =>
            {
                logger.LogInformation("{Message}", Strings.Get("Log_StartingBrand", brand));
                return await orchestrator.RunForBrandAsync(brand, dryRun, ct);
            }));

            RunSummaryPrinter.Print(results);
            return RunSummaryPrinter.HasUnexpectedFailures(results) ? 1 : 0;
        });

        return command;
    }
}
