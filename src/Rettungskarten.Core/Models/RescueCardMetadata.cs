namespace Rettungskarten.Core.Models;

/// <summary>
/// The JSON sidecar schema persisted next to every rescue card (or on its own, if the download failed).
/// This is the on-disk source of truth for a rescue card's metadata.
/// </summary>
public sealed record RescueCardMetadata(
    string Id,
    Brand Brand,
    string? ModelName,
    string? Variant,
    string? BodyType,
    int? BuildYearFrom,
    int? BuildYearTo,
    int? Doors,
    string? FuelType,
    string? LanguageCode,
    RescueCardStatus Status,
    string SourcePageUrl,
    string? DownloadUrl,
    string? FailureReason,
    ParseConfidence ParseConfidence,
    DateTimeOffset DiscoveredAtUtc,
    DateTimeOffset? DownloadedAtUtc,
    string? LocalPdfRelativePath,
    IReadOnlyList<string> SiblingModelIds,
    int? EstimatedFleetSize,
    BundlePriority BundlePriority);

public enum BundlePriority
{
    Unknown,
    Low,
    Medium,
    High
}
