using System.Text.RegularExpressions;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Text clean-up shared by the page-text layouts of combined PDFs produced with the same authoring
/// tool (Kia's and Nissan's older-models PDFs were both made "in Zusammenarbeit mit Moditech Rescue
/// Solutions" and share its page furniture). PdfPig emits every text block of a page in reading order
/// with no reliable separators, so a page header such as "Carnival Carnival (GQ) 1999-2006 08/2020 |
/// KIA Motors ... Moditech Rescue Solutions" arrives as one run of text: the model name printed twice
/// (once as the page's running title, once in the header line), the sheet's revision date, and the
/// publisher footer. These helpers strip exactly those parts, nothing model-specific.
/// </summary>
internal static class CombinedPdfHeaderText
{
    // The sheet's revision date is printed right before the footer's "|" separator (Kia "08/2020 |",
    // Nissan "10/2012 |", once even "04//2012 |") - it's the date of the sheet, not a build year, and
    // must not reach the year parsing.
    private static readonly Regex RevisionDate = new(@"\b\d{1,2}\s*/+\s*(?:19|20)\d{2}\s*\|", RegexOptions.Compiled);

    /// <summary>Removes the publisher footer (<paramref name="footer"/>, matched case-insensitively), the
    /// revision date in front of it and the "|" separator, and collapses whitespace.</summary>
    public static string StripFooter(string pageText, string footer)
    {
        var text = RevisionDate.Replace(pageText, " ");
        text = text.Replace(footer, " ", StringComparison.OrdinalIgnoreCase).Replace('|', ' ');
        return LabelText.Collapse(text);
    }

    /// <summary>
    /// Drops a word sequence that is immediately repeated at the start of <paramref name="name"/> -
    /// the running title printed in front of the header line: "Carnival Carnival" -> "Carnival",
    /// "Ceed sw PHEV Ceed sw PHEV" -> "Ceed sw PHEV", "NAVARA NAVARA King Cab" -> "NAVARA King Cab".
    /// Case-insensitive ("Xceed PHEV XCeed PHEV" -> "XCeed PHEV", keeping the header line's spelling).
    /// </summary>
    public static string CollapseRepeatedPrefix(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var length = words.Length / 2; length >= 1; length--)
        {
            var first = words.Take(length);
            var second = words.Skip(length).Take(length);
            if (first.SequenceEqual(second, StringComparer.OrdinalIgnoreCase))
            {
                return string.Join(' ', words.Skip(length));
            }
        }

        return name;
    }
}
