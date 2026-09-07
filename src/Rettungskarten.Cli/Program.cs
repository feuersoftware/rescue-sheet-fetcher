using System.CommandLine;
using Rettungskarten.Cli.Commands;

var verboseOption = new Option<bool>("--verbose")
{
    Description = "Ausführliche Logs",
    Recursive = true
};

var fetchCommand = new Command("fetch", "Ruft Daten von externen Quellen ab");
fetchCommand.Add(FetchRescueCardsCommand.Build(verboseOption));
fetchCommand.Add(FetchStockCommand.Build(verboseOption));

var listCommand = new Command("list", "Listet Informationen auf");
listCommand.Add(ListBrandsCommand.Build());

var inspectCommand = new Command("inspect", "Entwicklerwerkzeuge");
inspectCommand.Add(InspectXlsxCommand.Build());

var rootCommand = new RootCommand("Rettungskarten- und Fahrzeugbestand-Werkzeug für Feuerwehren (VW-Gruppe)");
rootCommand.Add(verboseOption);
rootCommand.Add(fetchCommand);
rootCommand.Add(PrioritizeCommand.Build());
rootCommand.Add(listCommand);
rootCommand.Add(inspectCommand);

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
