using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Rettungskarten.Core.Localization;
using Rettungskarten.Infrastructure.Stock;
using Rettungskarten.Infrastructure.Storage;

namespace Rettungskarten.Cli.Commands;

public static class FetchStockCommand
{
    public static Command Build(Option<bool> verboseOption)
    {
        var yearOption = new Option<int>("--year") { Description = Strings.Get("Option_Year_Description"), Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = Strings.Get("Option_Output_Stock_Description"),
            DefaultValueFactory = _ => Path.Combine("data", "stock")
        };

        var command = new Command("stock", Strings.Get("Command_Stock_Description"));
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
                Console.WriteLine(Strings.Get("Stock_Result", year, fetch.Parsed.Rows.Count, fetch.Parsed.UnparsedRowWarnings.Count));
                Console.WriteLine(Strings.Get("Stock_Source", fetch.Parsed.SourceUrl));
                Console.WriteLine(Strings.Get("Stock_License", fetch.Parsed.License));
                return 0;
            }
            catch (NotSupportedException ex)
            {
                Console.Error.WriteLine(Strings.Get("Stock_NotSupportedPrefix", ex.Message));
                return 1;
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(Strings.Get("Stock_ErrorPrefix", ex.Message));
                return 2;
            }
        });

        return command;
    }
}
