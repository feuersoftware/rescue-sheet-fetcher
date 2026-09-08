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
            // A continuation-only group whose first page's header we never saw (shouldn't normally
            // happen, but the document's structure is discovered heuristically, not guaranteed) -
            // fall back to the document's own id rather than losing the entry entirely.
            return new ParsedModelInfo(id, null, null, null, null, null, null, "EN", ParseConfidence.Unparsed);
        }

        var yearRange = ModelYearRangeTextHelper.Extract(headerText);
        var modelName = ExtractModelName(headerText);

        return new ParsedModelInfo(
            ModelName: modelName, Variant: headerText, BodyType: null,
            BuildYearFrom: yearRange.From, BuildYearTo: yearRange.To, Doors: null, FuelType: null,
            LanguageCode: "EN", ParseConfidence.Heuristic);
    }

    private static string ExtractModelName(string headerText)
    {
        var delimiterIndex = headerText.IndexOfAny([',', '/', '(']);
        var name = delimiterIndex > 0 ? headerText[..delimiterIndex] : headerText;
        return name.Trim();
    }

    private static string NormalizeId(string rawId) =>
        rawId.Replace(" ", string.Empty).ToUpperInvariant();
}
