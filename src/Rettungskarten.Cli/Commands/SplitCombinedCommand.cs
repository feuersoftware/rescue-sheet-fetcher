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
            var documentsFailed = 0;
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
                        // A combined document that was discovered but not downloaded (Fiat's and Abarth's
                        // collections, which robots.txt forbids) can't be fixed by re-running the same
                        // fetch - say why instead of pointing at it.
                        var notDownloaded = allCards.FirstOrDefault(c =>
                            c.Brand == layout.Brand && c.LocalPdfRelativePath is null && layout.IsCombinedEntry(c));
                        Console.Error.WriteLine(notDownloaded is null
                            ? Strings.Get("Split_NoCombinedEntriesFound", layout.Brand, BrandArgument.ToArgument(layout.Brand))
                            : Strings.Get("Split_CombinedEntryNotDownloaded", layout.Brand, notDownloaded.FailureReason ?? "-"));
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
                        if (splitCount is { } parts)
                        {
                            totalSplit += parts;
                            documentsProcessed++;
                        }
                        else
                        {
                            documentsFailed++;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Console.Error.WriteLine(Strings.Get("Split_DocumentFailed", combined.Variant ?? combined.Id, ex.Message));
                        documentsFailed++;
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
            // silently counted as "processed" in this summary. The other documents are still split,
            // but any failed one makes the exit code non-zero, so a script around this command can't
            // mistake a corrupted or no-longer-recognized document for a successful split.
            Console.WriteLine(Strings.Get("Split_Result", documentsProcessed, totalSplit));
            return documentsFailed > 0 ? 1 : 0;
        });
    }

    /// <summary>Splits one combined document and saves its per-model results; returns how many
    /// per-model cards were created, or null if the document failed (PDF missing on disk, or no models
    /// detected - the reason is printed, and the document's existing parts are left untouched).</summary>
    private static async Task<int?> SplitOneCombinedDocumentAsync(
        IRescueCardFileStore store, SiblingModelsConfig siblingConfig, ICombinedPdfLayout layout,
        IReadOnlyList<RescueCardMetadata> allCards, RescueCardMetadata combined, CancellationToken ct)
    {
        var documentName = combined.Variant ?? combined.ModelName ?? combined.Id;
        var pdfPath = store.GetPdfPath(combined);
        if (pdfPath is null || !File.Exists(pdfPath))
        {
            Console.Error.WriteLine(Strings.Get("Split_PdfMissing", documentName, pdfPath ?? "-"));
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(pdfPath, ct);
        var outcome = CombinedPdfSplitter.SplitDocument(bytes, layout);
        var splitResults = outcome.Parts;

        if (splitResults.Count == 0)
        {
            Console.Error.WriteLine(Strings.Get("Split_NoModelsDetected", documentName));
            return null;
        }

        // Neither is an error on its own (pages after a model's header page, a closing notes page),
        // but both are what a model whose header stopped matching looks like - so they are listed for
        // a person to check rather than silently becoming a wrongly labelled or missing card.
        var headerless = splitResults.SelectMany(s => s.HeaderlessPageNumbers).Order().ToList();
        if (headerless.Count > 0)
        {
            Console.Error.WriteLine(Strings.Get("Split_HeaderlessPagesJoined", documentName, PageList(headerless)));
        }

        if (outcome.UnassignedPageNumbers.Count > 0)
        {
            Console.Error.WriteLine(Strings.Get("Split_PagesNotAssigned", documentName, PageList(outcome.UnassignedPageNumbers)));
        }

        var saved = new List<RescueCardMetadata>(splitResults.Count);
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
                FuelType: FuelTypes.Normalize(split.Parsed.FuelType),
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

            saved.Add(await store.SaveAsync(newMetadata, split.PdfBytes, ct));
        }

        // Previous parts are removed only after the new ones are saved (a failure halfway through
        // must not leave the document with fewer parts than before), and only those the new split
        // didn't just overwrite in place.
        var current = saved.Select(m => (m.Id, RescueCardIdBuilder.BuildModelFolderSlug(m.ModelName))).ToHashSet();
        foreach (var previousPart in allCards.Where(c => c.DocumentScope == DocumentScope.SplitPart && c.SplitSourceId == combined.Id))
        {
            if (!current.Contains((previousPart.Id, RescueCardIdBuilder.BuildModelFolderSlug(previousPart.ModelName))))
            {
                await store.DeleteAsync(previousPart, ct);
            }
        }

        return splitResults.Count;
    }

    /// <summary>"3, 5-7, 12" for 1-based page numbers.</summary>
    public static string PageList(IReadOnlyList<int> pageNumbers)
    {
        var ranges = new List<string>();
        for (var i = 0; i < pageNumbers.Count;)
        {
            var start = pageNumbers[i];
            var end = start;
            while (i + 1 < pageNumbers.Count && pageNumbers[i + 1] == end + 1)
            {
                end = pageNumbers[++i];
            }

            ranges.Add(start == end ? $"{start}" : $"{start}-{end}");
            i++;
        }

        return string.Join(", ", ranges);
    }
}
