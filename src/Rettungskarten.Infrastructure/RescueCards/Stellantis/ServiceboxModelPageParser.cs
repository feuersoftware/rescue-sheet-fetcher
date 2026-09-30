using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.Stellantis;

/// <summary>One rescue sheet found on a Servicebox model page, before metadata parsing.</summary>
public sealed record ServiceboxSheetLink(string Label, string PdfUrl, string? FuelIcon, string LanguageCode);

/// <summary>
/// Extracts the rescue-sheet links from one Servicebox model page ("AIDE/{id}/FAD_AP_208.html").
/// Verified against every model page of all three brands: the page body is one layout cell holding,
/// in document order, section titles, headings and <c>table.text</c> tables, in two shapes that are
/// freely mixed on one page:
///
/// - "row tables" (older sheets): one row per sheet, the link text is the label
///   ("208 (1PIA) 2012→"; sometimes split over two links to the same PDF plus plain text:
///   "Expert (2PG9) verglaster kastenwagen 2007→"), the fuel pictogram sits in the row's second cell;
/// - "language tables" (sheets since ~2020): a heading with a fuel pictogram
///   ("208 Hybrid (1PP2) MHEV 2023 →") followed by a table of up to 30 per-language links whose text
///   is only the language name ("Deutsch", "English", "Français", ...).
///
/// Links are <c>href="#"</c> with the PDF in <c>onclick="window.open('../../../PDF_FAD/x.pdf', ...)"</c>.
/// Section titles decide what a table contains: "Rettungskarte (Rescue Sheet)" (Citroën's AMI page
/// uses the French "Fiche d'Aide à la Désincarcération (Rescue Sheet)") versus "Handbuch zur Rettung
/// (ERG)". The ERG section is excluded - and it has to be recognized by its title, because on the
/// 3008/5008 pages the ERG files have no "ERG" in their names ("e3008_(1PPD)_2023_BEV_de_DE.pdf"),
/// only the newer DS N°8 ones do ("ERG_DS_N8_..."). A heading without its own project code after a
/// headed table ("AWD (All-Wheel Drive): Allradantrieb" under "N°8 (1SQ8) 2025→") continues the last
/// heading's model, so the model is not lost for the second drivetrain variant.
///
/// Language: a language table yields its "Deutsch" link, or its "English" link if there is no German
/// one (<see cref="LanguagePreference"/>); a row yields the German/English PDF by the filename's locale
/// suffix ("_de_DE", "_de", "_en_GB", "_en"), a row whose PDF has no suffix counts as German (the page
/// is the de_DE locale). The link labels are not trusted blindly for other languages - one real row
/// links a "Россия" button to the English PDF - but the German/English buttons were consistent on
/// every page checked.
/// </summary>
public static class ServiceboxModelPageParser
{
    private static readonly Regex WindowOpenPdf = new(@"window\.open\(\s*'([^']+?\.pdf)'", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LocaleSuffix = new(@"[_-]([a-z]{2})(?:_[A-Z]{2})?\.pdf$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SectionMarker = new(
        @"(?<erg>Handbuch\s+zur\s+Rettung\s*(?:\(\s*ERG\s*\))?|\(\s*ERG\s*\))|(?<sheet>\(\s*Rescue\s+Sheet\s*\))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ProjectCode = new(@"\(\s*[0-9][A-Z][A-Z0-9]{2}\s*\)", RegexOptions.Compiled);

    /// <summary>The model part of a heading: up to and including the project code and its year.</summary>
    private static readonly Regex ModelHeadingPrefix = new(
        @"^.*?\(\s*[0-9][A-Z][A-Z0-9]{2}\s*\)(?:\s*(?:19|20)\d{2}\s*→?)?", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> LanguageButtons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Deutsch"] = "DE",
        ["English"] = "EN",
        ["Englisch"] = "EN"
    };

    public static IReadOnlyList<ServiceboxSheetLink> Parse(string html, string pageUrl)
    {
        var document = new HtmlParser().ParseDocument(html);
        var results = new List<ServiceboxSheetLink>();

        var inErgSection = false;
        string? lastModelHeading = null;

        foreach (var table in document.QuerySelectorAll("table.text"))
        {
            var (before, icon) = ReadPrecedingSiblings(table);

            var heading = before;
            var markers = SectionMarker.Matches(before);
            if (markers.Count > 0)
            {
                var last = markers[^1];
                inErgSection = last.Groups["erg"].Success;
                heading = before[(last.Index + last.Length)..].Trim();
                lastModelHeading = null;
            }

            if (inErgSection)
            {
                continue;
            }

            var anchors = PdfAnchors(table).ToList();
            if (anchors.Any(a => LanguageButtons.ContainsKey(a.Text)))
            {
                if (ProjectCode.IsMatch(heading))
                {
                    lastModelHeading = ModelHeadingPrefix.Match(heading).Value;
                }
                else if (lastModelHeading is not null)
                {
                    heading = $"{lastModelHeading} {heading}".Trim();
                }

                var buttons = anchors
                    .Where(a => LanguageButtons.ContainsKey(a.Text))
                    .Select(a => (a.FileUrl, Language: LanguageButtons[a.Text]))
                    .ToList();
                var preferred = LanguagePreference.PreferGermanThenEnglish(buttons, _ => string.Empty, b => b.Language).FirstOrDefault();
                if (preferred.FileUrl is not null)
                {
                    results.Add(new ServiceboxSheetLink(heading, HttpDownloadHelper.ResolveUrl(pageUrl, preferred.FileUrl), icon, preferred.Language));
                }

                continue;
            }

            foreach (var row in table.QuerySelectorAll("tr"))
            {
                var rowAnchors = PdfAnchors(row)
                    .Select(a => (a.FileUrl, Language: LanguageOf(a.FileUrl)))
                    .Where(a => a.Language is "DE" or "EN")
                    .ToList();
                var preferred = LanguagePreference.PreferGermanThenEnglish(rowAnchors, _ => string.Empty, a => a.Language).FirstOrDefault();
                if (preferred.FileUrl is null)
                {
                    continue;
                }

                var label = Collapse(row.QuerySelector("td")?.TextContent ?? string.Empty);
                var rowIcon = row.QuerySelector("img")?.GetAttribute("src");
                results.Add(new ServiceboxSheetLink(label, HttpDownloadHelper.ResolveUrl(pageUrl, preferred.FileUrl), rowIcon, preferred.Language));
            }
        }

        return results;
    }

    /// <summary>"DE"/"EN"/... from the filename's locale suffix; a file without one counts as German,
    /// the locale of the page it is linked from.</summary>
    internal static string LanguageOf(string fileUrl)
    {
        var match = LocaleSuffix.Match(HttpDownloadHelper.GetFileName(fileUrl));
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : "DE";
    }

    private static IEnumerable<(string Text, string FileUrl)> PdfAnchors(IElement container)
    {
        foreach (var anchor in container.QuerySelectorAll("a[onclick]"))
        {
            var match = WindowOpenPdf.Match(anchor.GetAttribute("onclick") ?? string.Empty);
            if (match.Success)
            {
                yield return (Collapse(anchor.TextContent), match.Groups[1].Value);
            }
        }
    }

    /// <summary>The text between the previous <c>table.text</c> (or the start of the cell) and this
    /// table, plus the image closest to the table (the heading's fuel pictogram).</summary>
    private static (string Text, string? Icon) ReadPrecedingSiblings(IElement table)
    {
        var parts = new List<string>();
        string? icon = null;
        for (var node = table.PreviousSibling; node is not null; node = node.PreviousSibling)
        {
            if (node is IElement element)
            {
                if (element.LocalName == "table" && element.ClassList.Contains("text"))
                {
                    break;
                }

                icon ??= element.LocalName == "img" ? element.GetAttribute("src") : element.QuerySelector("img")?.GetAttribute("src");
            }
            else if (node.NodeType != NodeType.Text)
            {
                continue; // the pages carry layout comments ("<!--100--><!--2-->") right before tables
            }

            parts.Add(node.TextContent);
        }

        parts.Reverse();
        return (Collapse(string.Join(' ', parts)), icon);
    }

    private static string Collapse(string value) =>
        new StringBuilder().AppendJoin(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToString();
}
