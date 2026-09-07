using System.Text.RegularExpressions;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

public readonly record struct YearRange(int? From, int? To);

/// <summary>
/// Shared helper for turning German generation/year-range phrases from rescue-card link text or
/// titles into a from/to year pair, e.g. "(2016-2021)" -> (2016, 2021), "ab 2021" -> (2021, null),
/// "bis 2021" -> (null, 2021), or a single bare year "2024" -> (2024, 2024).
/// </summary>
public static class ModelYearRangeTextHelper
{
    private static readonly Regex RangePattern = new(@"(19|20)\d{2}\s*-\s*(19|20)\d{2}", RegexOptions.Compiled);
    private static readonly Regex AbPattern = new(@"\bab\s+((?:19|20)\d{2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BisPattern = new(@"\bbis\s+((?:19|20)\d{2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SingleYearPattern = new(@"(19|20)\d{2}", RegexOptions.Compiled);

    public static YearRange Extract(string text)
    {
        var rangeMatch = RangePattern.Match(text);
        if (rangeMatch.Success)
        {
            var parts = rangeMatch.Value.Split('-', StringSplitOptions.TrimEntries);
            return new YearRange(int.Parse(parts[0]), int.Parse(parts[1]));
        }

        var abMatch = AbPattern.Match(text);
        if (abMatch.Success)
        {
            return new YearRange(int.Parse(abMatch.Groups[1].Value), null);
        }

        var bisMatch = BisPattern.Match(text);
        if (bisMatch.Success)
        {
            return new YearRange(null, int.Parse(bisMatch.Groups[1].Value));
        }

        var singleMatch = SingleYearPattern.Match(text);
        if (singleMatch.Success)
        {
            var year = int.Parse(singleMatch.Value);
            return new YearRange(year, year);
        }

        return new YearRange(null, null);
    }
}
