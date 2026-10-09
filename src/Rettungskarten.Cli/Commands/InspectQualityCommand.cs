using System.CommandLine;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Quality;
using Rettungskarten.Infrastructure.Config;
using Rettungskarten.Infrastructure.Storage;

namespace Rettungskarten.Cli.Commands;

/// <summary>
/// Checks already-persisted rescue-card metadata for anomalies that a naive "did discovery return any
/// results" check (like link-check.yml's dry-run) can't catch - e.g. a body type that's actually a
/// year, a fuel type that's just digits, or a duplicate id. See DataQualityChecker for the checks
/// themselves and the real bug (a shifted-field Audi filename-parsing bug) that motivated this command.
/// Works against discovery output alone (no PDF downloads needed) - BodyType/FuelType/Doors are already
/// populated by DiscoverAsync, so this runs fine straight after `fetch rescue-cards --dry-run`.
/// </summary>
public static class InspectQualityCommand
{
    public static Command Build()
    {
        var rescueCardsPathOption = new Option<string>("--rescue-cards-path")
        {
            Description = Strings.Get("Option_RescueCardsPath_Description"),
            DefaultValueFactory = _ => Path.Combine("data", "rescue-cards")
        };

        var command = new Command("quality", Strings.Get("Command_InspectQuality_Description"));
        command.Add(rescueCardsPathOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var rescueCardsPath = parseResult.GetValue(rescueCardsPathOption)!;
            var store = new FileSystemRescueCardStore(new RescueCardStoreOptions { RootPath = rescueCardsPath });

            var cards = await store.LoadAllAsync(ct);
            var unlistedModels = await ConfigLoader.LoadKbaUnlistedModelsAsync(ConfigLoader.DefaultKbaUnlistedModelsPath(), ct);
            var issues = DataQualityChecker.CheckAll(cards, unlistedModels);

            // Warnings (a flagged model that matched after all) ask for a review, they don't fail the run.
            foreach (var warning in issues.Where(i => i.Severity == DataQualityIssueSeverity.Warning))
            {
                Console.Error.WriteLine(Strings.Get("InspectQuality_WarningLine", warning.Brand, warning.CardId, warning.Description));
            }

            var errors = issues.Where(i => i.Severity == DataQualityIssueSeverity.Error).ToList();
            if (errors.Count == 0)
            {
                Console.WriteLine(Strings.Get("InspectQuality_NoIssuesFound", cards.Count));
                return 0;
            }

            foreach (var error in errors)
            {
                Console.Error.WriteLine(Strings.Get("InspectQuality_IssueLine", error.Brand, error.CardId, error.Description));
            }

            Console.Error.WriteLine(Strings.Get("InspectQuality_IssuesFound", errors.Count, cards.Count));
            return 1;
        });

        return command;
    }
}
