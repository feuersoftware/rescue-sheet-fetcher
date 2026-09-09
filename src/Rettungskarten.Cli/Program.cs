using System.CommandLine;
using System.Globalization;
using Rettungskarten.Cli.Commands;
using Rettungskarten.Core.Localization;

// --lang must be resolved before building the command tree: command/option Description strings are
// fixed at construction time (System.CommandLine renders --help before any command action runs), so
// there is no later point at which setting the culture would still affect help text.
//
// This pre-resolution goes through System.CommandLine itself (a throwaway single-option root with
// TreatUnmatchedTokensAsErrors = false) rather than a hand-rolled `Array.IndexOf(args, "--lang")`
// scan, so it recognizes every syntax the real parser does later (--lang de, --lang=de, --lang:de,
// prefix-matched forms, etc.) instead of silently missing some of them.
var langPreParseOption = new Option<string>("--lang");
var langPreParseRoot = new RootCommand { langPreParseOption };
langPreParseRoot.TreatUnmatchedTokensAsErrors = false;
var explicitLang = langPreParseRoot.Parse(args).GetValue(langPreParseOption);

// Validated case-insensitively in exactly one place: Strings.ParseLanguageOption. The visible
// --lang option added to the real command tree below intentionally has no AcceptOnlyFromAmong of
// its own - a second, case-sensitive validator there previously accepted "--lang DE" here (via
// ParseLanguageOption's case-insensitive match) but then rejected the same raw "DE" token when
// System.CommandLine re-validated it during the real parse, producing a confusing failure after
// the culture had already been switched.
if (explicitLang is not null && Strings.ParseLanguageOption(explicitLang) is null)
{
    Console.Error.WriteLine(Strings.Get("Error_InvalidLanguage", explicitLang));
    return 1;
}

var resolvedCulture = Strings.ParseLanguageOption(explicitLang);
Strings.OverrideCulture = resolvedCulture;

if (resolvedCulture is not null)
{
    // System.CommandLine's own built-in chrome (Usage:/Options:/Commands:, --version, error text)
    // follows the current thread culture, not our Strings.OverrideCulture - set both so an explicit
    // --lang consistently overrides the OS locale everywhere, not just in our own output.
    CultureInfo.CurrentCulture = resolvedCulture;
    CultureInfo.CurrentUICulture = resolvedCulture;
}

var verboseOption = new Option<bool>("--verbose")
{
    Description = Strings.Get("Option_Verbose_Description"),
    Recursive = true
};

var langOption = new Option<string>("--lang")
{
    Description = Strings.Get("Option_Lang_Description"),
    Recursive = true
};

var fetchCommand = new Command("fetch", Strings.Get("Command_Fetch_Description"));
fetchCommand.Add(FetchRescueCardsCommand.Build(verboseOption));
fetchCommand.Add(FetchStockCommand.Build(verboseOption));

var listCommand = new Command("list", Strings.Get("Command_List_Description"));
listCommand.Add(ListBrandsCommand.Build());

var inspectCommand = new Command("inspect", Strings.Get("Command_Inspect_Description"));
inspectCommand.Add(InspectXlsxCommand.Build());
inspectCommand.Add(InspectQualityCommand.Build());

var splitCommand = new Command("split", Strings.Get("Command_Split_Description"));
splitCommand.Add(SplitPorscheCommand.Build());

var rootCommand = new RootCommand(Strings.Get("App_Description"));
rootCommand.Add(verboseOption);
rootCommand.Add(langOption);
rootCommand.Add(fetchCommand);
rootCommand.Add(PrioritizeCommand.Build());
rootCommand.Add(listCommand);
rootCommand.Add(inspectCommand);
rootCommand.Add(splitCommand);

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
