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
/// `split &lt;brand|all&gt;`: post-processing step for combined "all models" PDFs (Porsche, Ford, ...),
/// separate from `fetch` the same way `prioritize` is - it operates on already-downloaded entries on
/// disk rather than fetching anything itself, so re-running it doesn't re-download the (large)
/// combined files. Which brands can be split, and how, is defined by <see cref="CombinedPdfLayouts"/>.
///
/// The combined entry itself is kept (<see cref="DocumentScope.Combined"/>); every part becomes its
/// own <see cref="DocumentScope.SplitPart"/> entry pointing back at it via
/// <see cref="RescueCardMetadata.SplitSourceId"/>. Re-running replaces a document's previous parts
/// rather than adding to them, so parts of a model that vanished from a newer version of the
/// document don't linger.
/// </summary>
public static class SplitCombinedCommand
{
    public static void Configure(Command splitCommand)
    {
        var brandArgument = new Argument<string>("brand")
        {
            Description = Strings.Get("Argument_SplitBrand_Description")
        };
        brandArgument.AcceptOnlyFromAmong(BrandArgument.AllowedValues());

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

        splitCommand.Add(brandArgument);
        splitCommand.Add(rescueCardsPathOption);
        splitCommand.Add(siblingConfigOption);

        splitCommand.SetAction(async (parseResult, ct) =>
        {
            var brandArg = parseResult.GetValue(brandArgument)!;
            var rescueCardsPath = parseResult.GetValue(rescueCardsPathOption)!;
            var siblingConfigPath = parseResult.GetValue(siblingConfigOption)!;

            var isAll = brandArg.Equals(BrandArgument.All, StringComparison.OrdinalIgnoreCase);
            var layouts = isAll
                ? CombinedPdfLayouts.All
                : CombinedPdfLayouts.For(BrandArgument.Resolve(brandArg)[0]);

            if (layouts.Count == 0)
            {
                Console.Error.WriteLine(Strings.Get("Split_NoLayoutForBrand", brandArg));
                return 1;
            }

            var store = new FileSystemRescueCardStore(new RescueCardStoreOptions { RootPath = rescueCardsPath });
            var siblingConfig = await ConfigLoader.LoadSiblingModelsAsync(siblingConfigPath, ct);
            var allCards = await store.LoadAllAsync(ct);

            var totalSplit = 0;
            var documentsProcessed = 0;
            var anyCombinedFound = false;

            foreach (var layout in layouts)
            {
                var combinedEntries = allCards
                    .Where(c => c.Brand == layout.Brand && c.LocalPdfRelativePath is not null && layout.IsCombinedEntry(c))
                    .ToList();

                if (combinedEntries.Count == 0)
                {
                    if (!isAll)
                    {
                        Console.Error.WriteLine(Strings.Get("Split_NoCombinedEntriesFound", layout.Brand, BrandArgument.ToArgument(layout.Brand)));
                    }

                    continue;
                }

                anyCombinedFound = true;
                foreach (var combined in combinedEntries)
                {
                    // One combined document failing (a corrupted download, or a future document neither
                    // PdfPig nor PDFsharp can open) must not abort processing of the others, and must
                    // not lose per-model cards already saved earlier in this loop - matches the same
                    // per-entry error isolation every brand's RescueCardOrchestrator run already gives
                    // per-model downloads.
                    try
                    {
                        var splitCount = await SplitOneCombinedDocumentAsync(store, siblingConfig, layout, allCards, combined, ct);
                        totalSplit += splitCount;
                        if (splitCount > 0)
                        {
                            documentsProcessed++;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Console.Error.WriteLine(Strings.Get("Split_DocumentFailed", combined.Variant ?? combined.Id, ex.Message));
                    }
                }

                await store.WriteBrandManifestAsync(layout.Brand, ct);
            }

            if (!anyCombinedFound)
            {
                if (isAll)
                {
                    Console.Error.WriteLine(Strings.Get("Split_NoCombinedEntriesFoundAnyBrand"));
                }

                return 1;
            }

            // documentsProcessed (not the number of combined entries) - a document skipped because its
            // PDF was missing on disk or because no models could be detected in it must not be
            // silently counted as "processed" in this summary, or a caller scripting around this
            // command's output would have no signal that one of the documents produced zero output.
            Console.WriteLine(Strings.Get("Split_Result", documentsProcessed, totalSplit));
            return 0;
        });
    }

    /// <summary>Splits one combined document and saves its per-model results; returns how many
    /// per-model cards were created (0 if the PDF was missing on disk or no models could be detected,
    /// in which case the document's existing parts are left untouched and it doesn't count as
    /// processed).</summary>
    private static async Task<int> SplitOneCombinedDocumentAsync(
        IRescueCardFileStore store, SiblingModelsConfig siblingConfig, ICombinedPdfLayout layout,
        IReadOnlyList<RescueCardMetadata> allCards, RescueCardMetadata combined, CancellationToken ct)
    {
        var pdfPath = store.GetPdfPath(combined);
        if (pdfPath is null || !File.Exists(pdfPath))
        {
            return 0;
        }

        var bytes = await File.ReadAllBytesAsync(pdfPath, ct);
        var splitResults = CombinedPdfSplitter.Split(bytes, layout);

        if (splitResults.Count == 0)
        {
            Console.Error.WriteLine(
                Strings.Get("Split_NoModelsDetected", combined.Variant ?? combined.ModelName ?? combined.Id));
            return 0;
        }

        foreach (var previousPart in allCards.Where(c => c.DocumentScope == DocumentScope.SplitPart && c.SplitSourceId == combined.Id))
        {
            await store.DeleteAsync(previousPart, ct);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var split in splitResults)
        {
            // split.DocumentId (the layout's group key, e.g. Porsche's internal "ENUS-01-710-0004") is
            // used here rather than split.Parsed.Variant - Variant is free text that two distinct models
            // can share (a length-capped header with a long common prefix), which would otherwise let
            // BuildId hash them to the same Id and one model's file silently overwrite the other's.
            // DocumentId is unique per document by the layout contract, so this can't happen.
            var newMetadata = new RescueCardMetadata(
                Id: RescueCardIdBuilder.BuildId(combined.Brand, split.Parsed, $"{combined.Id}|{split.DocumentId}"),
                Brand: combined.Brand,
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
                SiblingModelIds: siblingConfig.FindGroupIds(combined.Brand, split.Parsed.ModelName),
                EstimatedFleetSize: null,
                BundlePriority: BundlePriority.Unknown,
                DocumentScope: DocumentScope.SplitPart,
                ManufacturerGroup: combined.EffectiveManufacturerGroup,
                ChassisCode: split.Parsed.ChassisCode,
                SplitSourceId: combined.Id);

            await store.SaveAsync(newMetadata, split.PdfBytes, ct);
        }

        return splitResults.Count;
    }
}
