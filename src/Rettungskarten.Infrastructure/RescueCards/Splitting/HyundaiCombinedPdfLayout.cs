using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Layout of Hyundai's combined "Rettungsdatenblätter für Hyundai Modelle vor 11/2019" PDF (83 pages,
/// no bookmarks - verified). Three leading pages are a cover and a table of contents; after that every
/// model's sheet carries a header "{model}[, {body}] (Typ {code}, {years})" - "Accent, 3-Türer (Typ
/// LC, 2000-2006)", "(Grand) Santa Fe (Typ DM/NC, 2012-2018)", "Atos (Typ MX, MXL, MXI 1998-2008)".
/// Multi-page sheets (the high-voltage models: "1/5" ... "5/5") repeat the header on some pages and
/// not on others, so unkeyed pages join the sheet before them and a repeated header merges into its
/// first group. The contents pages list models as "•Accent (3-Türer) LC ab 2000" - no "(Typ", so they
/// are never mistaken for a sheet.
///
/// PdfPig emits the header after or before the running title and revision date in no fixed order
/// ("Atos Stand 06/2011 Atos (Typ MX...)"), so the header's name is the text right in front of
/// "(Typ", after the last "Stand MM/YYYY" or sentence end.
/// </summary>
public sealed class HyundaiCombinedPdfLayout : PageTextCombinedPdfLayout
{
    private static readonly Regex Header = new(
        @"(?:^|Stand\s+\d{1,2}/\d{4}\s+|[.:!]\s+)(?<name>(?:\([^()]{1,10}\)\s*)?[A-Za-z](?:(?!Stand\s)[^.:!•()]){0,40}?)\s*\(Typ\s+(?<typ>[^)]{1,40})\)",
        RegexOptions.Compiled);

    private static readonly Regex YearsInTyp = new(@"(?:ab\s+)?(?:19|20)\d{2}(?:\s*-\s*(?:19|20)\d{2})?", RegexOptions.Compiled);

    private static readonly Regex LpgOnlyNote = new(@"^\s*nur\s+.*?\bLPG\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override Brand Brand => Brand.Hyundai;

    protected override bool UnkeyedPagesContinuePreviousGroup => true;

    protected override string? TryGetPageKey(string pageText)
    {
        var header = FindHeader(pageText);
        return header is null ? null : $"{header.Value.Name} (Typ {header.Value.Typ})";
    }

    protected override ParsedModelInfo ParseGroup(string key, IReadOnlyList<string> pageTexts)
    {
        var header = FindHeader(key)!.Value;
        return ParseHeader(header.Name, header.Typ);
    }

    internal static ParsedModelInfo ParseHeader(string name, string typ)
    {
        var parts = HyundaiLabelParser.SplitModelName(name);
        var yearsMatch = YearsInTyp.Match(typ);
        var years = ModelYearRangeTextHelper.Extract(yearsMatch.Value, singleYearIsStartYear: true);

        // "LC, 2000-2006" / "AE HEV, ab 2016" / "MX, MXL, MXI 1998-2008": the codes before the years.
        var codes = (yearsMatch.Success ? typ[..yearsMatch.Index] : typ).Trim(' ', ',');
        var chassis = codes.Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return new ParsedModelInfo(
            ModelName: parts.ModelName,
            Variant: $"{name} (Typ {typ})",
            BodyType: parts.BodyType,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: parts.Doors,
            FuelType: parts.FuelType,
            LanguageCode: "DE",
            ParseConfidence: years.From is not null ? ParseConfidence.Heuristic : ParseConfidence.Unparsed,
            ChassisCode: chassis);
    }

    internal static (string Name, string Typ)? FindHeader(string text)
    {
        var match = Header.Match(LabelText.Collapse(text));
        if (!match.Success)
        {
            return null;
        }

        // A sheet valid only for the LPG version says so right in front of the header ("i30 Stand
        // 05/2010 nur i30 LPG i30, 5-Türer (Typ FD/FDH, ...)") - a note, not part of the name.
        var name = LpgOnlyNote.Replace(match.Groups["name"].Value, string.Empty).Trim();
        return (name, match.Groups["typ"].Value.Trim());
    }
}
