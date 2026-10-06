using System.Text.RegularExpressions;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

public readonly record struct YearRange(int? From, int? To);

/// <summary>
/// Shared helper for turning generation/year-range phrases from rescue-card link text or titles into
/// a from/to year pair, e.g. "(2016-2021)" -> (2016, 2021), "ab 2021" -> (2021, null),
/// "bis 2021" -> (null, 2021), a single bare year "2024" -> (2024, 2024), Porsche's English
/// "Model Year 2003 to Model Year 2005" / "from Model Year 2011" phrasing, or Bentley's
/// "(2021 - )" open-ended-dash phrasing for a model still in production.
///
/// Before matching, the text is normalized for the phrasings the non-VW sources add: en/em dashes
/// ("2012 – 2017"), a month in front of the year ("ab 03/2019", "11.2019 - 06.2023" - only the year
/// is kept, the schema has no month), and the German "Modelljahr"/"MJ" prefix ("ab Modelljahr 2023").
/// "seit"/"from"/"since" read like "ab", "vor"/"until"/"before" like "bis", and a range spelled out in
/// words ("von 03/2012 bis 11/2018", "ab 2019 bis 2023", "2008 bis 2012", "from 2019 to 2023") keeps
/// both of its ends.
///
/// A single bare year is ambiguous: on VW-style filenames it is the one year a sheet applies to, but
/// on most other brands' labels ("Captur 2 - 2021", "Spring 2024") it's the launch year of a model
/// still being built. <paramref name="singleYearIsStartYear"/> lets a caller pick the latter meaning.
/// </summary>
public static class ModelYearRangeTextHelper
{
    private static readonly Regex DashVariants = new(@"[‒–—―−]", RegexOptions.Compiled);
    private static readonly Regex MonthBeforeYear = new(@"\b(?:0?[1-9]|1[0-2])\s*[./]\s*(?=(?:19|20)\d{2}\b)", RegexOptions.Compiled);
    private static readonly Regex ModelYearPrefix = new(@"\b(?:Modelljahr|MJ)\s*(?=(?:19|20)\d{2}\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A start and an end year joined by a word - "von 2012 bis 2018", "ab 2019 bis 2023", Dacia's
    // "2008 bis 2012", "from 2019 to 2023" - is rewritten to "2012 - 2018" for RangePattern. Without
    // this, AbPattern/BisPattern below each returned only their own end of the range.
    private static readonly Regex WordedRange = new(
        @"(?:\b(?:ab|von|seit|from|since)\s+)?(?<!\d)((?:19|20)\d{2})\s+(?:bis|to|until)\s+((?:19|20)\d{2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RangePattern = new(@"((?:19|20)\d{2})\s*-\s*((?:19|20)\d{2})", RegexOptions.Compiled);
    private static readonly Regex AbPattern = new(@"\b(?:ab|seit|from|since)\s+((?:19|20)\d{2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BisPattern = new(@"\b(?:bis|vor|until|before)\s+((?:19|20)\d{2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
    private static readonly Regex SingleYearPattern = new(@"(?<!\d)(19|20)\d{2}(?!\d)", RegexOptions.Compiled);

    public static YearRange Extract(string text, bool singleYearIsStartYear = false)
    {
        text = Normalize(text);

        var rangeMatch = RangePattern.Match(text);
        if (rangeMatch.Success)
        {
            return new YearRange(int.Parse(rangeMatch.Groups[1].Value), int.Parse(rangeMatch.Groups[2].Value));
        }

        var modelYearRangeMatch = ModelYearRangePattern.Match(text);
        if (modelYearRangeMatch.Success)
        {
            return new YearRange(
                int.Parse(modelYearRangeMatch.Groups[1].Value), int.Parse(modelYearRangeMatch.Groups[2].Value));
        }

        var fromModelYearMatch = FromModelYearPattern.Match(text);
        if (fromModelYearMatch.Success)
        {
            return new YearRange(int.Parse(fromModelYearMatch.Groups[1].Value), null);
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

        var openEndedDashMatch = OpenEndedDashPattern.Match(text);
        if (openEndedDashMatch.Success)
        {
            return new YearRange(int.Parse(openEndedDashMatch.Groups[1].Value), null);
        }

        var singleMatch = SingleYearPattern.Match(text);
        if (singleMatch.Success)
        {
            var year = int.Parse(singleMatch.Value);
            return singleYearIsStartYear ? new YearRange(year, null) : new YearRange(year, year);
        }

        return new YearRange(null, null);
    }

    private static string Normalize(string text)
    {
        text = DashVariants.Replace(text, "-");
        text = MonthBeforeYear.Replace(text, string.Empty);
        text = ModelYearPrefix.Replace(text, string.Empty);
        return WordedRange.Replace(text, "$1 - $2");
    }
}
