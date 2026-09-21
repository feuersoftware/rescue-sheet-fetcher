using System.CommandLine;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Config;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Core.Naming;
using Rettungskarten.Infrastructure.Config;
using Rettungskarten.Infrastructure.RescueCards.Splitting;
using Rettungskarten.Infrastructure.Storage;

namespace Rettungskarten.Cli.Commands;

/// <summary>
/// Post-processing step for Porsche's combined "all models" PDF(s), separate from `fetch` the same
/// way `prioritize` is - it operates on already-downloaded entries on disk rather than fetching
/// anything itself, so re-running it doesn't re-download the (large) combined file.
/// </summary>
public static class SplitPorscheCommand
{
    public static Command Build()
    {
        var rescueCardsPathOption = new Option<string>("--rescue-cards-path")
        {
            Description = Strings.Get("Option_RescueCardsPath_Description"),
            DefaultValueFactory = _ => Path.Combine("data", "rescue-cards")
        };
        var siblingConfigOption = new Option<string>("--sibling-config")
        {
            Description = Strings.Get("Option_SiblingConfig_Description"),
            DefaultValueFactory = _ => ConfigLoader.DefaultSiblingModelsPath()
        };

        var command = new Command("porsche", Strings.Get("Command_SplitPorsche_Description"));
        command.Add(rescueCardsPathOption);
        command.Add(siblingConfigOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var rescueCardsPath = parseResult.GetValue(rescueCardsPathOption)!;
            var siblingConfigPath = parseResult.GetValue(siblingConfigOption)!;

            var store = new FileSystemRescueCardStore(new RescueCardStoreOptions { RootPath = rescueCardsPath });
            var siblingConfig = await ConfigLoader.LoadSiblingModelsAsync(siblingConfigPath, ct);

            var allCards = await store.LoadAllAsync(ct);
            // Only the two original combined documents have Variant text starting with "Rescue Data
            // Sheets" (the link text PorscheRescueCardSource discovered them under - matched the same
            // lenient way PorscheDocumentsPageParser/PorscheRescueCardSource themselves do, rather
            // than an exact string, so this stays consistent if that site text's casing/suffix ever
            // shifts slightly); every entry this command itself produces gets a real per-model header
            // as its Variant instead, so re-running the command (e.g. after one document failed to
            // split while another succeeded) never re-splits an already-split entry.
            var combinedEntries = allCards
                .Where(c => c.Brand == Brand.Porsche && c.LocalPdfRelativePath is not null &&
                    c.Variant != null && c.Variant.StartsWith("Rescue Data Sheets", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (combinedEntries.Count == 0)
            {
                Console.Error.WriteLine(Strings.Get("Split_Porsche_NoCombinedEntriesFound"));
                return 1;
            }

            var totalSplit = 0;
            var documentsProcessed = 0;
            foreach (var combined in combinedEntries)
            {
                // One combined document failing (a corrupted download, or a future document neither
                // PdfPig nor PDFsharp can open) must not abort processing of the other one, and must
                // not lose per-model cards already saved earlier in this loop - matches the same
                // per-entry error isolation every brand's RescueCardOrchestrator run already gives
                // per-model downloads.
                try
                {
                    var splitCount = await SplitOneCombinedDocumentAsync(store, siblingConfig, rescueCardsPath, combined, ct);
                    totalSplit += splitCount;
                    if (splitCount > 0)
                    {
                        documentsProcessed++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.Error.WriteLine(Strings.Get("Split_Porsche_DocumentFailed", combined.Variant ?? combined.Id, ex.Message));
                }
            }

            await store.WriteBrandManifestAsync(Brand.Porsche, ct);

            // documentsProcessed (not combinedEntries.Count) - a document skipped because its PDF was
            // missing on disk or because no models could be detected in it must not be silently
            // counted as "processed" in this summary, or a caller scripting around this command's
            // output would have no signal that one of the documents produced zero output.
            Console.WriteLine(Strings.Get("Split_Porsche_Result", documentsProcessed, totalSplit));
            return 0;
        });

        return command;
    }

    /// <summary>Splits one combined document and saves its per-model results; returns how many
    /// per-model cards were created (0 if the PDF was missing on disk or no models could be detected,
    /// in which case the caller should not delete the combined entry or count it as processed).</summary>
    private static async Task<int> SplitOneCombinedDocumentAsync(
        IRescueCardFileStore store, SiblingModelsConfig siblingConfig, string rescueCardsPath,
        RescueCardMetadata combined, CancellationToken ct)
    {
        var pdfPath = store.GetPdfPath(combined);
        if (pdfPath is null || !File.Exists(pdfPath))
        {
            return 0;
        }

        var bytes = await File.ReadAllBytesAsync(pdfPath, ct);
        var splitResults = PorscheCombinedPdfSplitter.Split(bytes);

        if (splitResults.Count == 0)
        {
            Console.Error.WriteLine(
                Strings.Get("Split_Porsche_NoModelsDetected", combined.Variant ?? combined.ModelName ?? combined.Id));
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var split in splitResults)
        {
            // split.DocumentId (the PDF's own internal id, e.g. "ENUS-01-710-0004") is used here
            // rather than split.Parsed.Variant - Variant is a length-capped free-text header, and two
            // distinct models can share a long enough common name/body-type prefix that the cap is
            // reached before either model's disambiguating text differs, which would otherwise let
            // BuildId hash them to the same Id and one model's file silently overwrite the other's.
            // DocumentId is unique by construction (it's literally the grouping key), so this can't happen.
            var newMetadata = new RescueCardMetadata(
                Id: RescueCardIdBuilder.BuildId(Brand.Porsche, split.Parsed, $"{combined.Id}|{split.DocumentId}"),
                Brand: Brand.Porsche,
                ModelName: split.Parsed.ModelName,
                Variant: split.Parsed.Variant,
                BodyType: split.Parsed.BodyType,
                BuildYearFrom: split.Parsed.BuildYearFrom,
                BuildYearTo: split.Parsed.BuildYearTo,
                Doors: split.Parsed.Doors,
                FuelType: split.Parsed.FuelType,
                LanguageCode: split.Parsed.LanguageCode,
                Status: RescueCardStatus.Downloaded,
                SourcePageUrl: combined.SourcePageUrl,
                DownloadUrl: combined.DownloadUrl,
                FailureReason: null,
                ParseConfidence: split.Parsed.ParseConfidence,
                DiscoveredAtUtc: now,
                DownloadedAtUtc: now,
                LocalPdfRelativePath: null,
                SiblingModelIds: siblingConfig.FindGroupIds(Brand.Porsche, split.Parsed.ModelName),
                EstimatedFleetSize: null,
                BundlePriority: BundlePriority.Unknown);

            await store.SaveAsync(newMetadata, split.PdfBytes, ct);
        }

        await store.DeleteAsync(combined, ct);
        return splitResults.Count;
    }
}
