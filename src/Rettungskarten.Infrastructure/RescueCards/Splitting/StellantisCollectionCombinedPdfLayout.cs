using System.Globalization;
using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Stellantis;
using UglyToad.PdfPig.Content;
using PigPdfDocument = UglyToad.PdfPig.PdfDocument;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Layout of the brand-wide "ShedaSoccorso" collection PDFs that fiat.de and abarth.de link (see
/// <see cref="StellantisBrandSiteRescueCardSource"/>): Fiat's "ShedaSoccorso_DE_01_01_12_T.pdf"
/// (47 pages, 43 sheets) and Abarth's "66_XXX_ShedaSoccorso_XXX_YY_ZZZ_DE_01_01.12_T.pdf" (6 pages,
/// 4 sheets). Both are built the same way (verified against the real files, 2026-10): a cover, a table
/// of contents ("Inhaltsverzeichnis") with one line per sheet - "FIAT GRANDE PUNTO LPG 3", "ABARTH 695
/// TRIBUTO FERRARI" - and the sheet number at the right margin, then exactly one page per sheet with that
/// number in its footer ("Fiat Group Automobiles SpA 09/2010 6"). There are no bookmarks, and the sheets
/// print their model name only as part of an image, so the table of contents is the only text naming the
/// models. Its lines are rebuilt from word positions (like <see cref="DaihatsuCombinedPdfLayout"/>):
/// the number and its line aren't always on the same baseline (up to 8pt apart), and a date can sit on a
/// baseline of its own, so every word is given to the sheet number closest to it vertically.
///
/// Each footer number is matched to its table-of-contents line, so a page that isn't where the table
/// says doesn't shift every later model by one. A page whose footer has no number from the table (none
/// in the real files) belongs to no part and is reported by <c>split</c>.
///
/// The table's lines end with the door count as a bare digit ("FIAT STILO 3") and drop the "ab"/"bis"
/// of a date ("FIAT DOBLÒ NATURAL POWER 12/2009" is the Doblò built until 12/2009); the sheet itself
/// prints "(BIS_12/2009)"/"(AB_01/2010)", so years come from the sheet (a year the line states itself
/// is kept for a sheet without one), the rest from the line (<see cref="ParseEntry"/>). The footer
/// dates are revision dates and are never read as years.
///
/// Every page is read once (<see cref="ReadPages"/>) and serves both the table of contents and the
/// footer lookup - PdfPig parses a page's content again on every pass.
///
/// Registered for Fiat and Abarth only: fiat.de/professional links the same Fiat collection, but its
/// sheets are Fiat passenger cars - splitting that copy would file them under Fiat Professional.
/// </summary>
public sealed class StellantisCollectionCombinedPdfLayout(Brand brand, string brandPrefix) : ICombinedPdfLayout
{
    private const string ContentsHeading = "Inhaltsverzeichnis";
    private const string FooterAnchor = "SpA";
    private const double SameLineTolerance = 3.0;

    /// <summary>Furthest a table-of-contents word may sit from its sheet number's baseline. The real
    /// offsets are 0-8pt; the lines are ~35pt apart.</summary>
    private const double MaxEntryOffset = 12.0;

    /// <summary>Sheet numbers stand right of this fraction of the page width (x≈533-542 of 595).</summary>
    private const double NumberColumnStart = 0.8;

    private static readonly Regex SheetNumber = new(@"^\d{1,3}$", RegexOptions.Compiled);
    private static readonly Regex EntryDate = new(@"\s*\b\d{1,2}/(?:19|20)\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex TrailingDoorCount = new(@"\s([2-5])$", RegexOptions.Compiled);
    private static readonly Regex SheetDate = new(@"\((AB|BIS)_\d{1,2}/((?:19|20)\d{2})\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Brand Brand => brand;

    public bool IsCombinedEntry(RescueCardMetadata entry) => entry.DocumentScope == DocumentScope.Combined;

    public IReadOnlyList<CombinedPdfPageGroup> DetectGroups(PigPdfDocument document)
    {
        var pages = ReadPages(document);
        var entries = ReadContents(pages);
        var groups = new List<CombinedPdfPageGroup>();
        var seen = new HashSet<int>();

        foreach (var page in pages)
        {
            if (FindFooterSheetNumber(page.Words) is not { } number ||
                !entries.TryGetValue(number, out var entry) ||
                !seen.Add(number))
            {
                continue;
            }

            groups.Add(new CombinedPdfPageGroup(
                $"{number} {entry}",
                ParseEntry(entry, page.Text, brandPrefix),
                [page.Number - 1])); // PdfPig is 1-based, PDFsharp page indices are 0-based
        }

        return groups;
    }

    private sealed record PageContent(int Number, double Width, string Text, IReadOnlyList<Word> Words);

    private static List<PageContent> ReadPages(PigPdfDocument document) =>
        document.GetPages().Select(p => new PageContent(p.Number, p.Width, p.Text, p.GetWords().ToList())).ToList();

    /// <summary>Sheet number -> table-of-contents line ("FIAT DOBLÒ NATURAL POWER 12/2009").</summary>
    internal static IReadOnlyDictionary<int, string> ReadContents(PigPdfDocument document) =>
        ReadContents(ReadPages(document));

    private static Dictionary<int, string> ReadContents(IReadOnlyList<PageContent> pages)
    {
        var entries = new Dictionary<int, string>();
        foreach (var page in pages)
        {
            var words = page.Words
                .Select(w => (Word: w, Text: w.Text.Trim('.')))
                .Where(w => w.Text.Length > 0)
                .ToList();
            if (!words.Any(w => w.Text.Equals(ContentsHeading, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var numberColumn = page.Width * NumberColumnStart;
            var numbers = words
                .Where(w => w.Word.BoundingBox.Left >= numberColumn && SheetNumber.IsMatch(w.Text))
                .ToList();
            if (numbers.Count == 0)
            {
                continue;
            }

            var wordsByNumber = numbers.ToDictionary(n => n.Word, _ => new List<(Word Word, string Text)>());
            foreach (var word in words)
            {
                if (word.Word.BoundingBox.Left >= numberColumn || word.Text.Equals(ContentsHeading, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var closest = numbers.MinBy(n => Math.Abs(n.Word.BoundingBox.Bottom - word.Word.BoundingBox.Bottom));
                if (Math.Abs(closest.Word.BoundingBox.Bottom - word.Word.BoundingBox.Bottom) <= MaxEntryOffset)
                {
                    wordsByNumber[closest.Word].Add(word);
                }
            }

            foreach (var (numberWord, text) in numbers)
            {
                var line = string.Join(' ', wordsByNumber[numberWord]
                    .OrderBy(w => w.Word.BoundingBox.Left)
                    .Select(w => w.Text));
                if (line.Length > 0)
                {
                    entries.TryAdd(int.Parse(text, CultureInfo.InvariantCulture), line);
                }
            }
        }

        return entries;
    }

    /// <summary>The number at the end of the footer line ("Fiat Group Automobiles SpA 09/2010 6"), or
    /// null for a page without that footer (cover, table of contents). Only the footer line is read: a
    /// sheet's drawing can carry stray digits of its own (the door badge "3" on the Grande Punto 3).</summary>
    private static int? FindFooterSheetNumber(IReadOnlyList<Word> words)
    {
        var anchor = words.FirstOrDefault(w => w.Text == FooterAnchor);
        if (anchor is null)
        {
            return null;
        }

        var number = words
            .Where(w => w.BoundingBox.Left > anchor.BoundingBox.Right &&
                        Math.Abs(w.BoundingBox.Bottom - anchor.BoundingBox.Bottom) <= SameLineTolerance &&
                        SheetNumber.IsMatch(w.Text))
            .MaxBy(w => w.BoundingBox.Left);
        return number is null ? null : int.Parse(number.Text, CultureInfo.InvariantCulture);
    }

    internal static ParsedModelInfo ParseEntry(string entry, string sheetText, string brandPrefix)
    {
        // The table's own date is read from the sheet instead, together with its "ab"/"bis"; its
        // trailing door count ("FIAT STILO 3") is taken here, so it can't end up in the model name.
        var label = EntryDate.Replace(entry, string.Empty).Trim();
        var doorCount = TrailingDoorCount.Match(label);
        if (doorCount.Success)
        {
            label = label[..doorCount.Index];
        }

        var parsed = StellantisRescueSheetLabelParser.Parse(label, string.Empty, [brandPrefix]);

        int? from = null, to = null;
        foreach (Match date in SheetDate.Matches(sheetText))
        {
            var year = int.Parse(date.Groups[2].Value, CultureInfo.InvariantCulture);
            if (date.Groups[1].Value.Equals("AB", StringComparison.OrdinalIgnoreCase))
            {
                from ??= year;
            }
            else
            {
                to ??= year;
            }
        }

        return parsed with
        {
            Variant = entry,
            Doors = doorCount.Success ? int.Parse(doorCount.Groups[1].Value, CultureInfo.InvariantCulture) : parsed.Doors,
            BuildYearFrom = from ?? parsed.BuildYearFrom,
            BuildYearTo = to ?? parsed.BuildYearTo
        };
    }
}
