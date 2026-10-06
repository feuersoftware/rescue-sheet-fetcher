using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Config;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Core.Naming;

namespace Rettungskarten.Core.Orchestration;

/// <summary>
/// Brand-agnostic discover-download-store pipeline shared by every brand. A failure discovering or
/// downloading one brand's cards must never abort the whole run — see <see cref="RunForBrandAsync"/>.
///
/// A brand may have more than one registered source (smart: Mercedes' portal for the models up to
/// 2021, smart's own site for the Geely-era models since 2022). Every source is discovered
/// independently, each entry is downloaded through the source that discovered it, and one source
/// failing never discards another's cards - the brand is still reported as
/// <see cref="BrandRunOutcome.DiscoveryFailed"/> in that case so the weekly link check notices.
/// </summary>
public sealed class RescueCardOrchestrator(
    IEnumerable<IRescueCardSource> sources,
    IRescueCardFileStore store,
    SiblingModelsConfig siblingConfig,
    ILogger<RescueCardOrchestrator> logger,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<BrandRunResult> RunForBrandAsync(Brand brand, bool dryRun, CancellationToken ct)
    {
        var brandSources = sources.Where(s => s.Brand == brand).ToList();
        if (brandSources.Count == 0)
        {
            return BrandRunResult.NotImplemented(brand, Strings.Get("Orchestrator_NoSourceRegistered"));
        }

        var discovered = new List<(IRescueCardSource Source, RescueCardEntry Entry)>();
        var failureNotes = new List<string>();
        var notImplementedNotes = new List<string>();

        foreach (var source in brandSources)
        {
            try
            {
                var entries = await source.DiscoverAsync(ct);
                discovered.AddRange(entries.Select(e => (source, e)));
            }
            catch (NotSupportedException ex)
            {
                logger.LogInformation("{Message}", Strings.Get("Orchestrator_NotImplementedLog", brand, ex.Message));
                notImplementedNotes.Add(ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "{Message}", Strings.Get("Orchestrator_DiscoveryFailedLog", brand));
                failureNotes.Add(ex.Message);
            }
        }

        if (notImplementedNotes.Count == brandSources.Count)
        {
            return BrandRunResult.NotImplemented(brand, string.Join("; ", notImplementedNotes));
        }

        if (failureNotes.Count > 0 && discovered.Count == 0)
        {
            return BrandRunResult.DiscoveryFailed(brand, [], string.Join("; ", failureNotes));
        }

        var results = new List<ModelRunResult>(discovered.Count);
        try
        {
            await DownloadAndStoreAsync(brand, dryRun, discovered, results, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything else here (the store failing to write - disk full, a locked or too-long path) is
            // still confined to this brand: brands run in parallel, and one brand's exception escaping
            // would discard every other brand's result and the run summary.
            logger.LogError(ex, "{Message}", Strings.Get("Orchestrator_BrandFailedLog", brand));
            failureNotes.Add(ex.Message);
        }

        return failureNotes.Count > 0
            ? BrandRunResult.DiscoveryFailed(brand, results, string.Join("; ", failureNotes))
            : BrandRunResult.Completed(brand, results);
    }

    private async Task DownloadAndStoreAsync(
        Brand brand, bool dryRun, List<(IRescueCardSource Source, RescueCardEntry Entry)> discovered,
        List<ModelRunResult> results, CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        foreach (var (source, entry) in discovered)
        {
            ct.ThrowIfCancellationRequested();

            var download = entry.DownloadUrl is null
                ? RescueCardDownloadResult.Fail(Strings.Get("FailureReason_NoDownloadUrl"))
                : dryRun
                    ? RescueCardDownloadResult.Fail(Strings.Get("FailureReason_DryRunSkipped"))
                    : await TryDownloadAsync(source, entry, ct);

            var status = entry.DownloadUrl is null
                ? RescueCardStatus.Failed
                : download.Success
                    ? RescueCardStatus.Downloaded
                    : RescueCardStatus.MetadataOnly;

            var metadata = new RescueCardMetadata(
                Id: RescueCardIdBuilder.BuildId(brand, entry.Parsed, entry.RawFileNameOrLabel),
                Brand: brand,
                ModelName: entry.Parsed.ModelName,
                Variant: entry.Parsed.Variant,
                BodyType: entry.Parsed.BodyType,
                BuildYearFrom: entry.Parsed.BuildYearFrom,
                BuildYearTo: entry.Parsed.BuildYearTo,
                Doors: entry.Parsed.Doors,
                FuelType: FuelTypes.Normalize(entry.Parsed.FuelType),
                LanguageCode: entry.Parsed.LanguageCode,
                Status: status,
                SourcePageUrl: entry.SourcePageUrl,
                DownloadUrl: entry.DownloadUrl,
                FailureReason: download.FailureReason,
                ParseConfidence: entry.Parsed.ParseConfidence,
                DiscoveredAtUtc: now,
                DownloadedAtUtc: download.Success ? now : null,
                LocalPdfRelativePath: null,
                SiblingModelIds: siblingConfig.FindGroupIds(brand, entry.Parsed.ModelName),
                EstimatedFleetSize: null,
                BundlePriority: BundlePriority.Unknown,
                DocumentScope: entry.Scope,
                ManufacturerGroup: entry.ManufacturerGroup,
                ChassisCode: entry.Parsed.ChassisCode);

            await store.SaveAsync(metadata, download.Success ? download.Content : null, ct);
            results.Add(new ModelRunResult(entry, status, download.FailureReason));
        }

        if (!dryRun)
        {
            await store.WriteBrandManifestAsync(brand, ct);
        }
    }

    private async Task<RescueCardDownloadResult> TryDownloadAsync(
        IRescueCardSource source, RescueCardEntry entry, CancellationToken ct)
    {
        try
        {
            return await source.DownloadAsync(entry, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Message}", Strings.Get("Orchestrator_DownloadFailedLog", entry.Brand, entry.RawFileNameOrLabel));
            return RescueCardDownloadResult.Fail(ex.Message);
        }
    }
}
