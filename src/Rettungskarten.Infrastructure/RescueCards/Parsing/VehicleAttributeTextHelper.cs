using System.Text.RegularExpressions;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Shared helper for pulling a body type/fuel type out of free-text titles or headers against a known
/// vocabulary (Porsche's page-header text, Škoda's model-page link titles) - neither source publishes
/// these as a separate field. Takes the *last* vocabulary match in the text rather than the first:
/// a body-style word can also appear earlier as part of the model designation itself (e.g. "Boxster
/// Spyder" names the model, but a later "Cabriolet" in the same text is the actual body type; "Škoda
/// Kodiaq iV SUV 2024 5d hybrid" names an "iV" trim before its actual fuel type, "hybrid", at the end).
/// Returns null - never a guess - when no vocabulary word is present, so an unrecognized future body
/// style/fuel type shows up as missing data rather than as a silently wrong value.
/// </summary>
public static class VehicleAttributeTextHelper
{
    public static string? ExtractLastVocabularyMatch(string text, IReadOnlyList<string> vocabulary)
    {
        // Longer/more specific phrases must be tried before a shorter one that's a substring of it
        // (e.g. "PHEV HYBRID" before bare "Hybrid") so a compound term isn't preempted by its own tail.
        var pattern = string.Join('|', vocabulary.OrderByDescending(v => v.Length).Select(Regex.Escape));
        var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
        return matches.Count > 0 ? matches[^1].Value : null;
    }

    private static readonly Regex DoorCountPattern = new(
        @"(\d{1,2})\s*-?\s*(?:d\b|T[üu]rer)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Extracts a door count from either the shared "Nd" convention (e.g. "5d") or Škoda's own
    /// German "N-Türer" phrasing (e.g. "3-Türer").</summary>
    public static int? ExtractDoors(string text)
    {
        var match = DoorCountPattern.Match(text);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }
}
