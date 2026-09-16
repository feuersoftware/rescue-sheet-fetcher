namespace Rettungskarten.Core.Models;

/// <summary>
/// Best-effort structured information extracted from a rescue card's filename or link/label text.
/// No source publishes this as structured data, so every field may be null and <see cref="ParseConfidence"/>
/// records how much to trust the result.
/// </summary>
public sealed record ParsedModelInfo(
    string? ModelName,
    string? Variant,
    string? BodyType,
    int? BuildYearFrom,
    int? BuildYearTo,
    int? Doors,
    string? FuelType,
    string? LanguageCode,
    ParseConfidence ParseConfidence);

public enum ParseConfidence
{
    /// <summary>Matched against a known model/body-type/fuel-type dictionary.</summary>
    High,

    /// <summary>Derived heuristically (e.g. leftover tokens, generic year-range regex).</summary>
    Heuristic,

    /// <summary>Could not be parsed meaningfully; raw text was kept as-is.</summary>
    Unparsed
}
