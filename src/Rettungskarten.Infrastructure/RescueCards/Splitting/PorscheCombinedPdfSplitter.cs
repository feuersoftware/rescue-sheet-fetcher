using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;
using PigPdfDocument = UglyToad.PdfPig.PdfDocument;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Splits Porsche's combined "all models" rescue-data PDF into one small PDF per model, matching the
/// one-file-per-model convention every other brand already follows.
///
/// The document has no PDF outline/bookmarks (verified empirically), so model boundaries are found in
/// the page text instead: every content page carries a footer "ID no. {id} Version no. {n} Page {p}
/// [of {total}]", and the *first* page of each model additionally carries a header
/// "Porsche AG, {model name/derivatives} {body type} {Model Year range}". Consecutive pages sharing
/// the same ID belong to one model (some models span several pages - the ID is what actually groups
/// them, "Page x of y" is corroborating but not required). The one page without an ID (the leading
/// legal-notice page) is simply skipped.
/// </summary>
public static class PorscheCombinedPdfSplitter
{
    // The digit-group width varies between documents (the main file uses e.g. "ENUS-01-710-0001",
    // the classic file "ENGB-01-710-041" - three digits, not four, in the last group) so each numeric
    // segment's length is left flexible rather than hardcoded.
    private static readonly Regex IdPattern = new(
        @"ID\s*no\.?\s*([A-Za-z]{2,4}\s*-\s*\d{1,4}\s*-\s*\d{1,4}\s*-\s*\d{1,4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
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

    /// <summary>One model's extracted rescue-data pages, already assembled into a standalone PDF.
    /// <paramref name="DocumentId"/> is the document's own internal id (e.g. "ENUS-01-710-0004") - it
    /// is guaranteed unique per group by construction (it's literally the grouping key), unlike
    /// <c>Parsed.Variant</c>, which is a length-capped free-text header that two distinct models can
    /// share a long common prefix of (same name/body-type/year-range text before their respective "ID
    /// no." footers) - callers that need a stable, collision-resistant identifier (e.g. for building a
    /// persisted card id) should use <paramref name="DocumentId"/>, not <c>Parsed.Variant</c>.</summary>
    public sealed record SplitResult(ParsedModelInfo Parsed, string DocumentId, byte[] PdfBytes);

    public static IReadOnlyList<SplitResult> Split(byte[] combinedPdfBytes)
    {
        var groups = ExtractGroups(combinedPdfBytes);
        if (groups.Count == 0)
        {
            return [];
        }

        var results = new List<SplitResult>(groups.Count);

        using var sourceStream = new MemoryStream(combinedPdfBytes);
        using var sourceDocument = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Import);

        foreach (var (id, parsed, pageIndices) in groups)
        {
            using var outputDocument = new PdfDocument();
            foreach (var pageIndex in pageIndices)
            {
                outputDocument.AddPage(sourceDocument.Pages[pageIndex]);
            }

            using var outputStream = new MemoryStream();
            outputDocument.Save(outputStream, closeStream: false);
            results.Add(new SplitResult(parsed, id, outputStream.ToArray()));
        }

        return results;
    }

    private static List<(string Id, ParsedModelInfo Parsed, List<int> PageIndices)> ExtractGroups(byte[] pdfBytes)
    {
        var pageIndicesById = new Dictionary<string, List<int>>();
        var headerById = new Dictionary<string, string>();
        var encounterOrder = new List<string>();

        using (var document = PigPdfDocument.Open(pdfBytes))
        {
            foreach (var page in document.GetPages())
            {
                var text = page.Text;
                var idMatch = IdPattern.Match(text);
                if (!idMatch.Success)
                {
                    continue; // e.g. the leading legal-notice page - not a model page
                }

                var id = NormalizeId(idMatch.Groups[1].Value);
                if (!pageIndicesById.TryGetValue(id, out var pageIndices))
                {
                    pageIndices = [];
                    pageIndicesById[id] = pageIndices;
                    encounterOrder.Add(id);
                }

                pageIndices.Add(page.Number - 1); // PdfPig is 1-based, PDFsharp page indices are 0-based

                if (!headerById.ContainsKey(id))
                {
                    var headerText = ExtractHeaderText(text);
                    if (headerText is not null)
                    {
                        headerById[id] = headerText;
                    }
                }
            }
        }

        var groups = new List<(string, ParsedModelInfo, List<int>)>(encounterOrder.Count);
        foreach (var id in encounterOrder)
        {
            groups.Add((id, ParseHeader(id, headerById.GetValueOrDefault(id)), pageIndicesById[id]));
        }

        return groups;
    }

    private static string? ExtractHeaderText(string pageText)
    {
        var markerIndex = pageText.IndexOf(HeaderMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
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
            // not. The PDF content itself is still complete and correctly saved either way.
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
        return FixKnownTypo(name.Trim());
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
