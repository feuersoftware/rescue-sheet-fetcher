namespace Rettungskarten.Core.Models;

/// <summary>
/// One rescue card discovered by a brand source, before download is attempted.
///
/// <see cref="DownloadUrl"/> is the stable URL the card is known by - for most sources the PDF itself,
/// but for sources whose real PDF URL is short-lived or only reachable through an extra hop (BMW's
/// signed S3 links, Mercedes' per-card detail pages) it is that hop's stable
/// entry point, and the source resolves the real PDF URL only at download time (see
/// <c>RescueCardSourceBase.ResolveDownloadUrlAsync</c>).
/// </summary>
public sealed record RescueCardEntry(
    Brand Brand,
    string SourcePageUrl,
    string? DownloadUrl,
    string RawFileNameOrLabel,
    ParsedModelInfo Parsed,
    DocumentScope Scope = DocumentScope.Single,
    ManufacturerGroup? ManufacturerGroupOverride = null)
{
    /// <summary>The group persisted for this entry: the source's per-entry override if it set one
    /// (only smart needs this), otherwise the brand's default from <see cref="BrandGroups"/>.</summary>
    public ManufacturerGroup ManufacturerGroup => ManufacturerGroupOverride ?? BrandGroups.GroupOf(Brand);
}

public sealed record RescueCardDownloadResult(
    bool Success,
    byte[]? Content,
    string? FailureReason,
    int? HttpStatusCode)
{
    public static RescueCardDownloadResult Ok(byte[] content) => new(true, content, null, 200);

    public static RescueCardDownloadResult Fail(string reason, int? statusCode = null) =>
        new(false, null, reason, statusCode);
}
