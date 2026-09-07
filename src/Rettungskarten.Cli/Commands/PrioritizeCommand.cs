using System.CommandLine;
using System.Text.Json;
using Rettungskarten.Core.Config;
using Rettungskarten.Core.Models;
using Rettungskarten.Core.Priority;
using Rettungskarten.Infrastructure.Config;
using Rettungskarten.Infrastructure.Storage;

namespace Rettungskarten.Cli.Commands;

public static class PrioritizeCommand
{
    public static Command Build()
    {
        var rescueCardsPathOption = new Option<string>("--rescue-cards-path")
        {
            DefaultValueFactory = _ => Path.Combine("data", "rescue-cards")
        };
        var stockPathOption = new Option<string>("--stock-path")
        {
            DefaultValueFactory = _ => Path.Combine("data", "stock")
        };
        var stockYearOption = new Option<int?>("--stock-year")
        {
            Description = "Bestandsjahr (Standard: neuestes vorhandenes)"
        };
        var aliasesOption = new Option<string>("--aliases")
        {
            DefaultValueFactory = _ => ConfigLoader.DefaultModelAliasesPath()
        };

        var command = new Command(
            "prioritize", "Ordnet Rettungskarten anhand des KBA-Fahrzeugbestands eine Bündel-Priorität zu");
        command.Add(rescueCardsPathOption);
        command.Add(stockPathOption);
        command.Add(stockYearOption);
        command.Add(aliasesOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var rescueCardsPath = parseResult.GetValue(rescueCardsPathOption)!;
            var stockPath = parseResult.GetValue(stockPathOption)!;
            var stockYear = parseResult.GetValue(stockYearOption);
            var aliasesPath = parseResult.GetValue(aliasesOption)!;

            var store = new FileSystemRescueCardStore(new RescueCardStoreOptions { RootPath = rescueCardsPath });
            var stockStore = new FileSystemVehicleStockStore(new VehicleStockStoreOptions { RootPath = stockPath });

            var stock = await stockStore.LoadAsync(stockYear, ct);
            if (stock is null)
            {
                Console.Error.WriteLine("Kein Fahrzeugbestand gefunden - zuerst 'fetch stock --year <jahr>' ausführen.");
                return 1;
            }

            var aliases = await ConfigLoader.LoadModelAliasesAsync(aliasesPath, ct);
            var calculator = new BundlePriorityCalculator(aliases);

            var cards = await store.LoadAllAsync(ct);
            var updated = new List<RescueCardMetadata>(cards.Count);

            foreach (var card in cards)
            {
                var match = calculator.Calculate(card.Brand, card.ModelName, stock.Rows);
                var newCard = card with { EstimatedFleetSize = match.EstimatedFleetSize, BundlePriority = match.Priority };
                await store.UpdateMetadataAsync(newCard, ct);
                updated.Add(newCard);
            }

            await WriteReportAsync(rescueCardsPath, updated, ct);

            Console.WriteLine($"{updated.Count} Rettungskarten priorisiert (Bestand: FZ12 {stock.Year}).");
            var byPriority = updated.GroupBy(c => c.BundlePriority).ToDictionary(g => g.Key, g => g.Count());
            foreach (var priority in Enum.GetValues<BundlePriority>())
            {
                Console.WriteLine($"  {priority}: {byPriority.GetValueOrDefault(priority)}");
            }

            return 0;
        });

        return command;
    }

    private static async Task WriteReportAsync(string rescueCardsPath, List<RescueCardMetadata> updated, CancellationToken ct)
    {
        var fullRescueCardsPath = Path.GetFullPath(
            rescueCardsPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var dataRoot = Path.GetDirectoryName(fullRescueCardsPath) ?? fullRescueCardsPath;
        var reportPath = Path.Combine(dataRoot, "priority-report.json");

        var report = updated
            .OrderByDescending(c => c.EstimatedFleetSize ?? -1)
            .Select(c => new
            {
                c.Brand, c.ModelName, c.Variant, c.Status, c.EstimatedFleetSize, c.BundlePriority
            });

        await using var stream = File.Create(reportPath);
        await JsonSerializer.SerializeAsync(stream, report, JsonDefaults.Options, ct);

        Console.WriteLine($"Report: {reportPath}");
    }
}
