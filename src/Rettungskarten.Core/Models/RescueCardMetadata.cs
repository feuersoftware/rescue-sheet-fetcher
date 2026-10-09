using System.Text.Json.Serialization;

namespace Rettungskarten.Core.Models;

/// <summary>
/// The JSON sidecar schema persisted next to every rescue card (or on its own, if the download failed).
/// This is the on-disk source of truth for a rescue card's metadata. The trailing optional fields were
/// added after the first release, so they default to "absent" for sidecars written before them.
/// <see cref="SplitSourceId"/> is set only on <see cref="DocumentScope.SplitPart"/> entries and holds
/// the <see cref="Id"/> of the combined entry they were cut from.
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
    BundlePriority BundlePriority,
    DocumentScope DocumentScope = DocumentScope.Single,
    ManufacturerGroup? ManufacturerGroup = null,
    string? ChassisCode = null,
    string? SplitSourceId = null)
{
    /// <summary>The persisted group, or the brand's default for sidecars written before the field
    /// existed.</summary>
    [JsonIgnore]
    public ManufacturerGroup EffectiveManufacturerGroup => ManufacturerGroup ?? BrandGroups.GroupOf(Brand);
}

public enum BundlePriority
{
    Unknown,
    Low,
    Medium,
    High
}
