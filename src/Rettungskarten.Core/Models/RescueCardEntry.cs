namespace Rettungskarten.Core.Models;

/// <summary>
/// One rescue card discovered by a brand source, before download is attempted.
/// </summary>
public sealed record RescueCardEntry(
    Brand Brand,
    string SourcePageUrl,
    string? DownloadUrl,
    string RawFileNameOrLabel,
    ParsedModelInfo Parsed);

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
