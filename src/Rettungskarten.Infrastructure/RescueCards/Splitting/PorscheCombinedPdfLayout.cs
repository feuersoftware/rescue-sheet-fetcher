using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Config;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Layout of Porsche's combined "all models" rescue-data PDF (current and classic models).
///
/// The document has no PDF outline/bookmarks (verified empirically), so model boundaries are found in
/// the page text instead: every content page carries a footer "ID no. {id} Version no. {n} Page {p}
/// [of {total}]", and the *first* page of each model additionally carries a header
/// "Porsche AG, {model name/derivatives} {body type} {Model Year range}". Pages sharing the same ID
/// belong to one model (some models span several pages - the ID is what actually groups them, "Page x
/// of y" is corroborating but not required). The one page without an ID (the leading legal-notice
/// page) is simply skipped.
///
/// The 2025 edition appends sheets in the newer Euro NCAP layout, which differ in three ways (all
/// verified against the real document, 2026-10-07): their ID is a filename-like
/// "WP0_Porsche_911__Coupé_2025_2d_GD_GB_V001"; the Panamera (G3) sheets print a placeholder "ID no.
/// GB-?" instead of an ID, so they're keyed by their header (which every one of their pages repeats);
/// and the header reads "Porsche AG, 911 2 door, 4 seater Coupe, as from model year 2025", which
/// PdfPig glues to "9112 door" - the door count is cut off the model name.
/// </summary>
public sealed class PorscheCombinedPdfLayout : PageTextCombinedPdfLayout
{
    // The digit-group width varies between documents (the main file uses e.g. "ENUS-01-710-0001",
    // the classic file "ENGB-01-710-041" - three digits, not four, in the last group) so each numeric
    // segment's length is left flexible rather than hardcoded.
    private static readonly Regex IdPattern = new(
        @"ID\s*no\.?\s*(WP0_\w+?_V\d{3}|[A-Za-z]{2,4}\s*-\s*\d{1,4}\s*-\s*\d{1,4}\s*-\s*\d{1,4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HeaderKeyPattern = new(
        @"Porsche AG,\s*(.{1,150}?as from model year\s*\d{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TrailingDoorCount = new(@"\d\s*door$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const string HeaderMarker = "Porsche AG,";
    private const int HeaderWindowLength = 200;

    // Every body style actually seen across a real production run of both combined PDFs (current +
    // classic models) - see VehicleAttributeTextHelper for why the *last* match in the header wins.
    private static readonly string[] BodyTypeVocabulary =
    [
        "Sport Turismo", "Stationwagon", "Hardtop-Cabriolet", "Convertible-D (Hardtop)",
        "Cabriolet", "Roadster", "Convertible", "Coupé", "Coupe", "Targa", "Spyder", "Saloon",
        "Sedan", "SUV"
    ];

    // Porsche typed the wrong ID into the footer of one page: page 3 of 6 of the Cayenne E-Hybrid
    // sheet (ENUS-01-710-0039) says "ENUS-01-710-0040", an ID no other page uses. Matched together with
    // its page marker, so a real future sheet under that ID isn't merged into the Cayenne.
    private static readonly IReadOnlyList<(string WrongId, string PageMarker, string Id)> PageKeyCorrections =
    [
        ("ENUS-01-710-0040", "Page 3 of 6", "ENUS-01-710-0039")
    ];

    // Sheets whose pages never carry the "Porsche AG," header - the model appears only in the photos
    // and, for the E-Hybrid supplements, in a heading PdfPig scrambles (see ParseHeader). Named by hand
    // in Config/porsche-headerless-sheets.json after looking at each document, so a change on Porsche's
    // side is a config edit; an ID that isn't listed there still falls back to the ID itself instead
    // of a guess (and `inspect quality` reports it as an unmatched model).
    private static readonly Lazy<IReadOnlyDictionary<string, PorscheHeaderlessSheet>> HeaderlessSheets = new(() =>
        ConfigLoader.LoadPorscheHeaderlessSheets(ConfigLoader.DefaultPorscheHeaderlessSheetsPath())
            .Sheets.ToDictionary(s => s.DocumentId, StringComparer.OrdinalIgnoreCase));

    public override Brand Brand => Brand.Porsche;

    /// <summary>Also recognizes combined entries fetched before sources marked them
    /// <see cref="DocumentScope.Combined"/>: only the two original documents have Variant text starting
    /// with "Rescue Data Sheets" (the link text PorscheRescueCardSource discovers them under), while
    /// every split part gets a real per-model header as its Variant.</summary>
    public override bool IsCombinedEntry(RescueCardMetadata entry) =>
        entry.DocumentScope == DocumentScope.Combined ||
        (entry.DocumentScope == DocumentScope.Single && entry.Variant is not null &&
            entry.Variant.StartsWith("Rescue Data Sheets", StringComparison.OrdinalIgnoreCase));

    protected override string? TryGetPageKey(string pageText) => GetPageKey(pageText);

    protected override ParsedModelInfo ParseGroup(string key, IReadOnlyList<string> pageTexts) => Parse(key, pageTexts);

    internal static string? GetPageKey(string pageText)
    {
        var idMatch = IdPattern.Match(pageText);
        if (idMatch.Success)
        {
            var id = NormalizeId(idMatch.Groups[1].Value);
            var correction = PageKeyCorrections.FirstOrDefault(c => c.WrongId == id && pageText.Contains(c.PageMarker, StringComparison.Ordinal));
            return correction.Id ?? id;
        }

        var headerMatch = HeaderKeyPattern.Match(pageText);
        return headerMatch.Success ? headerMatch.Groups[1].Value : null;
    }

    internal static ParsedModelInfo Parse(string key, IReadOnlyList<string> pageTexts)
    {
        var headerText = pageTexts.Select(ExtractHeaderText).FirstOrDefault(h => h is not null);

        // Only for a sheet that really has no header: should Porsche reuse one of these IDs for a sheet
        // with a header, the header wins over the hand-made name.
        if (string.IsNullOrWhiteSpace(headerText) && HeaderlessSheets.Value.TryGetValue(key, out var sheet))
        {
            return new ParsedModelInfo(sheet.ModelName, null, sheet.BodyType, null, null, null, null, "EN", ParseConfidence.High);
        }

        return ParseHeader(key, headerText);
    }

    private static string? ExtractHeaderText(string pageText)
    {
        var markerIndex = pageText.IndexOf(HeaderMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        // Some 2025 sheets print their header twice, glued: "Porsche AG, 911Porsche AG, 9112 door, ...".
        var repeatedIndex = pageText.IndexOf(HeaderMarker, markerIndex + HeaderMarker.Length, StringComparison.Ordinal);
        if (repeatedIndex >= 0 && repeatedIndex - markerIndex < 40)
        {
            markerIndex = repeatedIndex;
        }

        var start = markerIndex + HeaderMarker.Length;
        var length = Math.Min(HeaderWindowLength, pageText.Length - start);
        return pageText.Substring(start, length).Trim();
    }

    private static ParsedModelInfo ParseHeader(string id, string? headerText)
    {
        if (string.IsNullOrWhiteSpace(headerText))
        {
            // A handful of real documents in the combined PDF (E-Hybrid identification/safety-marking
            // supplement pages, e.g. for the Panamera and Cayenne E-Hybrid) never carry the "Porsche
            // AG," header at all on any page - the model name only appears deep in the body text, e.g.
            // "...Vehicle identification and marking[Model] identification features...". This was
            // investigated (not just assumed): that text is NOT safely extractable because PdfPig's
            // page.Text word ordering visibly scrambles multi-column content on these specific pages
            // (one real example glued "Panamera" and "Cayenne E-Hybrid" - two different models -
            // directly together with no separator, which a naive "text before 'identification
            // features'" pattern would misreport as the model name). Guessing wrong here is worse than
            // this honest fallback to the document's own id: a firefighter trusting a mislabeled
            // rescue card is a real safety risk that an "unparsed" card asking for manual lookup is
            // not. The PDF content itself is still complete and correctly saved either way. The sheets
            // known today are named by hand in HeaderlessSheets; this fallback only catches new ones.
            return new ParsedModelInfo(id, null, null, null, null, null, null, "EN", ParseConfidence.Unparsed);
        }

        var yearRange = ModelYearRangeTextHelper.Extract(headerText);
        var modelName = ExtractModelName(headerText);
        var bodyType = VehicleAttributeTextHelper.ExtractLastVocabularyMatch(headerText, BodyTypeVocabulary);

        // Doors and fuel type aren't available as separate data here: unlike VW/Audi/SEAT/Cupra's
        // filenames, Porsche's header text never states a door count, and any fuel/drivetrain
        // information (e.g. "E-Hybrid") is already baked into the free-text model name itself rather
        // than appearing as its own field - so there's nothing further to safely extract.
        return new ParsedModelInfo(
            ModelName: modelName, Variant: headerText, BodyType: bodyType,
            BuildYearFrom: yearRange.From, BuildYearTo: yearRange.To, Doors: null, FuelType: null,
            LanguageCode: "EN", ParseConfidence.Heuristic);
    }

    internal static string ExtractModelName(string headerText)
    {
        var delimiterIndex = headerText.IndexOfAny([',', '/', '(']);
        var name = delimiterIndex > 0 ? headerText[..delimiterIndex] : headerText;
        return FixKnownTypo(TrailingDoorCount.Replace(name.Trim(), string.Empty).Trim());
    }

    // Porsche's own combined PDF misspells "Boxster" as "Boxter" on several of its pages (verified
    // against the real document - not a text-extraction artifact of PdfPig). Left uncorrected, this
    // splits what's really one model across "boxster" and "boxter" (and "boxter-spyder") folders on a
    // source typo. "Boxter" never legitimately appears as a substring of any other model name, so a
    // plain case-insensitive replace is safe here.
    private static string FixKnownTypo(string modelName) =>
        modelName.Replace("Boxter", "Boxster", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeId(string rawId) =>
        rawId.Replace(" ", string.Empty).ToUpperInvariant();
}
