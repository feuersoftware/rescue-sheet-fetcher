using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Rettungskarten.Infrastructure.Stock;
using Rettungskarten.Infrastructure.Storage;

namespace Rettungskarten.Cli.Commands;

public static class FetchStockCommand
{
    public static Command Build(Option<bool> verboseOption)
    {
        var yearOption = new Option<int>("--year") { Description = "Bezugsjahr (Stichtag 1. Januar)", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Zielverzeichnis für den Fahrzeugbestand",
            DefaultValueFactory = _ => Path.Combine("data", "stock")
        };

        var command = new Command("stock", "Lädt den KBA-Fahrzeugbestand (FZ12) für ein Jahr");
        command.Add(yearOption);
        command.Add(outputOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var year = parseResult.GetValue(yearOption);
            var output = parseResult.GetValue(outputOption)!;
            var verbose = parseResult.GetValue(verboseOption);

            using var services = CompositionRoot.Build(verbose);
            var source = services.GetRequiredService<KbaVehicleStockSource>();
            var store = new FileSystemVehicleStockStore(new VehicleStockStoreOptions { RootPath = output });

            try
            {
                var fetch = await source.FetchWithRawAsync(year, ct);
                await store.SaveAsync(fetch.Parsed, fetch.RawContent, fetch.RawFileName, ct);

                Console.WriteLine();
                Console.WriteLine($"FZ12 {year}: {fetch.Parsed.Rows.Count} Modellreihen geladen, {fetch.Parsed.UnparsedRowWarnings.Count} Warnung(en).");
                Console.WriteLine($"Quelle: {fetch.Parsed.SourceUrl}");
                Console.WriteLine($"Lizenz: {fetch.Parsed.License}");
                return 0;
            }
            catch (NotSupportedException ex)
            {
                Console.Error.WriteLine($"Nicht unterstützt: {ex.Message}");
                return 1;
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"Fehler: {ex.Message}");
                return 2;
            }
        });

        return command;
    }
}
