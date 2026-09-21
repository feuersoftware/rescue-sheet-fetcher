using System.Text.RegularExpressions;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

public readonly record struct YearRange(int? From, int? To);

/// <summary>
/// Shared helper for turning generation/year-range phrases from rescue-card link text or titles into
/// a from/to year pair, e.g. "(2016-2021)" -> (2016, 2021), "ab 2021" -> (2021, null),
/// "bis 2021" -> (null, 2021), a single bare year "2024" -> (2024, 2024), Porsche's English
/// "Model Year 2003 to Model Year 2005" / "from Model Year 2011" phrasing, or Bentley's
/// "(2021 - )" open-ended-dash phrasing for a model still in production.
/// </summary>
public static class ModelYearRangeTextHelper
{
    private static readonly Regex RangePattern = new(@"(19|20)\d{2}\s*-\s*(19|20)\d{2}", RegexOptions.Compiled);
    private static readonly Regex AbPattern = new(@"\bab\s+((?:19|20)\d{2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BisPattern = new(@"\bbis\s+((?:19|20)\d{2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ModelYearRangePattern = new(
        @"(?:from\s+)?Model\s+Year\s+((?:19|20)\d{2})\s+to\s+(?:Model\s+Year\s+)?((?:19|20)\d{2})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // No \b anchors: PdfPig-extracted text sometimes glues adjacent words together with no space in
    // either direction (e.g. "SUVfrom Model Year 2011Page 1"), and \b never matches between two
    // "word" characters - which includes digit-to-letter transitions like "2011" -> "Page".
    private static readonly Regex FromModelYearPattern = new(
        @"from\s+Model\s+Year\s+((?:19|20)\d{2})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // A year immediately followed by a dash with no second year after it, e.g. Bentley's
    // "(2021 - )" - the negative lookahead is a safety net (RangePattern above already handles a real
    // "YYYY - YYYY" range and is checked first), not load-bearing on its own.
    private static readonly Regex OpenEndedDashPattern = new(
        @"((?:19|20)\d{2})\s*-\s*(?!(?:19|20)\d{2})", RegexOptions.Compiled);
    private static readonly Regex SingleYearPattern = new(@"(19|20)\d{2}", RegexOptions.Compiled);

    public static YearRange Extract(string text)
    {
        var rangeMatch = RangePattern.Match(text);
        if (rangeMatch.Success)
        {
            var parts = rangeMatch.Value.Split('-', StringSplitOptions.TrimEntries);
            return new YearRange(int.Parse(parts[0]), int.Parse(parts[1]));
        }

        var modelYearRangeMatch = ModelYearRangePattern.Match(text);
        if (modelYearRangeMatch.Success)
        {
            return new YearRange(
                int.Parse(modelYearRangeMatch.Groups[1].Value), int.Parse(modelYearRangeMatch.Groups[2].Value));
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

        var fromModelYearMatch = FromModelYearPattern.Match(text);
        if (fromModelYearMatch.Success)
        {
            return new YearRange(int.Parse(fromModelYearMatch.Groups[1].Value), null);
        }

        var openEndedDashMatch = OpenEndedDashPattern.Match(text);
        if (openEndedDashMatch.Success)
        {
            return new YearRange(int.Parse(openEndedDashMatch.Groups[1].Value), null);
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
