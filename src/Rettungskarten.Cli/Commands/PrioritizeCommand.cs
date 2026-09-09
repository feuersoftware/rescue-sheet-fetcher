using System.CommandLine;
using System.Text;
using System.Text.Json;
using Rettungskarten.Core.Config;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Core.Priority;
using Rettungskarten.Core.Reporting;
using Rettungskarten.Infrastructure.Config;
using Rettungskarten.Infrastructure.Storage;

namespace Rettungskarten.Cli.Commands;

public static class PrioritizeCommand
{
    public static Command Build()
    {
        var rescueCardsPathOption = new Option<string>("--rescue-cards-path")
        {
            Description = Strings.Get("Option_RescueCardsPath_Description"),
            DefaultValueFactory = _ => Path.Combine("data", "rescue-cards")
        };
        var stockPathOption = new Option<string>("--stock-path")
        {
            Description = Strings.Get("Option_StockPath_Description"),
            DefaultValueFactory = _ => Path.Combine("data", "stock")
        };
        var stockYearOption = new Option<int?>("--stock-year")
        {
            Description = Strings.Get("Option_StockYear_Description")
        };
        var aliasesOption = new Option<string>("--aliases")
        {
            Description = Strings.Get("Option_Aliases_Description"),
            DefaultValueFactory = _ => ConfigLoader.DefaultModelAliasesPath()
        };

        var command = new Command("prioritize", Strings.Get("Command_Prioritize_Description"));
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
                Console.Error.WriteLine(Strings.Get("Prioritize_NoStockFound"));
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

            Console.WriteLine(Strings.Get("Prioritize_Result", updated.Count, stock.Year));
            var byPriority = updated.GroupBy(c => c.BundlePriority).ToDictionary(g => g.Key, g => g.Count());
            foreach (var priority in Enum.GetValues<BundlePriority>())
            {
                Console.WriteLine($"  {DisplayText.For(priority)}: {byPriority.GetValueOrDefault(priority)}");
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
        var jsonPath = Path.Combine(dataRoot, "priority-report.json");
        var csvPath = Path.Combine(dataRoot, "priority-report.csv");

        var sorted = updated.OrderByDescending(c => c.EstimatedFleetSize ?? -1).ToList();
        var report = sorted.Select(c => new
        {
            c.Brand, c.ModelName, c.Variant, c.Status, c.EstimatedFleetSize, c.BundlePriority
        });

        await using (var stream = File.Create(jsonPath))
        {
            await JsonSerializer.SerializeAsync(stream, report, JsonDefaults.Options, ct);
        }

        // A UTF-8 BOM is required here (File.WriteAllTextAsync's default encoding omits it): this file
        // is meant to be double-clicked open in Excel on Windows by non-developer staff, and Excel
        // falls back to the system ANSI codepage without a BOM, mangling brand/model names that
        // contain non-ASCII characters (e.g. Škoda's "Š"/"ř").
        await File.WriteAllTextAsync(csvPath, PriorityReportCsvFormatter.Format(sorted), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);

        Console.WriteLine(Strings.Get("Prioritize_ReportLabel", jsonPath));
        Console.WriteLine(Strings.Get("Prioritize_CsvReportLabel", csvPath));
    }
}
