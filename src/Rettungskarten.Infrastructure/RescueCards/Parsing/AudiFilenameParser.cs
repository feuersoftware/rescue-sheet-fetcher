using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses Audi's rescue-card filenames. These mostly follow the same underscore-delimited convention
/// as VW/SEAT/Cupra (see <see cref="PositionalFilenameParser"/>), but Audi's own CMS occasionally
/// deviates in two ways - found via a real production run against every currently discoverable Audi
/// file, not hypothetically:
///
/// - A fuel-type value is sometimes split across two tokens instead of one, e.g.
///   "..._Hybrid_(Electric)_DE.pdf" instead of "..._Hybrid-Electric_DE.pdf" (seen on the A6/A7/A8
///   "Limousine"-variant files). Left unhandled, this shifts every field from body type onward by one
///   position - e.g. the model year silently ends up in <c>BodyType</c> instead of
///   <c>BuildYearFrom</c>. Any token that's entirely wrapped in parentheses is merged into the token
///   immediately before it, so this becomes one "Hybrid (Electric)" fuel-type value instead.
/// - A trailing numeral appears immediately before the language-code token on at least one file
///   ("..._Electric_1_DE.pdf") - almost certainly a de-duplication suffix from Audi's asset CMS, not
///   real data (fuel type, the field in that position, is never just digits across any brand's
///   filenames). Dropped whenever found there.
///
/// Without this normalization these files' body type/year/fuel type all end up shifted and silently
/// wrong rather than merely missing - see <c>AudiFilenameParserTests</c> for the exact real filenames
/// this was found against.
/// </summary>
public static class AudiFilenameParser
{
    private static readonly Regex FullyParenthesized = new(@"^\(.*\)$", RegexOptions.Compiled);
    private static readonly Regex PureDigits = new(@"^\d+$", RegexOptions.Compiled);

    public static ParsedModelInfo Parse(string fileNameOrUrl) =>
        PositionalFilenameParser.Parse(NormalizeQuirks(PositionalFilenameParser.Tokenize(fileNameOrUrl)));

    private static string[] NormalizeQuirks(string[] tokens)
    {
        var merged = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            if (merged.Count > 0 && FullyParenthesized.IsMatch(token))
            {
                merged[^1] = $"{merged[^1]} {token}";
            }
            else
            {
                merged.Add(token);
            }
        }

        // The fuel-type token (immediately before the language code) is never a bare number for any
        // brand's filenames - a numeral there is a spurious CMS suffix, not data.
        if (merged.Count >= 2 && PureDigits.IsMatch(merged[^2]))
        {
            merged.RemoveAt(merged.Count - 2);
        }

        return merged.ToArray();
    }
}
