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
        var source = sources.FirstOrDefault(s => s.Brand == brand);
        if (source is null)
        {
            return BrandRunResult.NotImplemented(brand, Strings.Get("Orchestrator_NoSourceRegistered"));
        }

        IReadOnlyList<RescueCardEntry> entries;
        try
        {
            entries = await source.DiscoverAsync(ct);
        }
        catch (NotSupportedException ex)
        {
            logger.LogInformation("{Message}", Strings.Get("Orchestrator_NotImplementedLog", brand, ex.Message));
            return BrandRunResult.NotImplemented(brand, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Message}", Strings.Get("Orchestrator_DiscoveryFailedLog", brand));
            return BrandRunResult.DiscoveryFailed(brand, ex);
        }

        var results = new List<ModelRunResult>(entries.Count);
        var now = _time.GetUtcNow();

        foreach (var entry in entries)
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
                FuelType: entry.Parsed.FuelType,
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
                BundlePriority: BundlePriority.Unknown);

            await store.SaveAsync(metadata, download.Success ? download.Content : null, ct);
            results.Add(new ModelRunResult(entry, download.Success, download.FailureReason));
        }

        if (!dryRun)
        {
            await store.WriteBrandManifestAsync(brand, ct);
        }

        return BrandRunResult.Completed(brand, results);
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
