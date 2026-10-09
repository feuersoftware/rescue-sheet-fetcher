using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Rettungskarten.Core.Models;
using PigPdfDocument = UglyToad.PdfPig.PdfDocument;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Splits a combined multi-model rescue-sheet PDF into one small PDF per model, matching the
/// one-file-per-model convention of every per-model brand. Model boundaries come from the brand's
/// <see cref="ICombinedPdfLayout"/>; this class only reads the document with PdfPig (text and
/// bookmarks) and copies each group's pages into a standalone document with PDFsharp (which, unlike
/// PdfPig, can write).
/// </summary>
public static class CombinedPdfSplitter
{
    /// <summary>One model's extracted pages, already assembled into a standalone PDF.
    /// <paramref name="DocumentId"/> is the group's <see cref="CombinedPdfPageGroup.Key"/> - unique
    /// per document by contract, so callers building a persisted id should use it rather than any
    /// free-text field of <paramref name="Parsed"/>. <paramref name="HeaderlessPageNumbers"/> (1-based)
    /// are the pages included only because they follow the model's first page.</summary>
    public sealed record SplitResult(ParsedModelInfo Parsed, string DocumentId, byte[] PdfBytes, IReadOnlyList<int> HeaderlessPageNumbers);

    /// <summary>The parts, plus every page (1-based) after the first part's first page that no part
    /// contains. Leading pages (cover, contents, legal notice) are expected to belong to no model and
    /// aren't listed; a page further in that ends up nowhere is either furniture or a model whose
    /// header wasn't recognized. <paramref name="PageNumberingMismatchFirstPages"/> (1-based) are the
    /// first pages of the parts whose own page numbering doesn't add up.</summary>
    public sealed record SplitOutcome(
        IReadOnlyList<SplitResult> Parts, IReadOnlyList<int> UnassignedPageNumbers, IReadOnlyList<int> PageNumberingMismatchFirstPages);

    public static IReadOnlyList<SplitResult> Split(byte[] combinedPdfBytes, ICombinedPdfLayout layout) =>
        SplitDocument(combinedPdfBytes, layout).Parts;

    public static SplitOutcome SplitDocument(byte[] combinedPdfBytes, ICombinedPdfLayout layout)
    {
        IReadOnlyList<CombinedPdfPageGroup> groups;
        int pageCount;
        using (var document = PigPdfDocument.Open(combinedPdfBytes))
        {
            groups = layout.DetectGroups(document);
            pageCount = document.NumberOfPages;
        }

        if (groups.Count == 0)
        {
            return new SplitOutcome([], [], []);
        }

        var results = new List<SplitResult>(groups.Count);

        using var sourceStream = new MemoryStream(combinedPdfBytes);
        using var sourceDocument = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Import);

        foreach (var group in groups)
        {
            using var outputDocument = new PdfDocument();
            foreach (var pageIndex in group.PageIndices)
            {
                outputDocument.AddPage(sourceDocument.Pages[pageIndex]);
            }

            using var outputStream = new MemoryStream();
            outputDocument.Save(outputStream, closeStream: false);
            results.Add(new SplitResult(
                group.Parsed, group.Key, outputStream.ToArray(),
                (group.HeaderlessPageIndices ?? []).Select(i => i + 1).Order().ToList()));
        }

        var mismatched = groups.Where(g => g.PageNumberingMismatch).Select(g => g.PageIndices.Min() + 1).Order().ToList();
        return new SplitOutcome(results, FindUnassignedPages(groups, pageCount), mismatched);
    }

    internal static IReadOnlyList<int> FindUnassignedPages(IReadOnlyList<CombinedPdfPageGroup> groups, int pageCount)
    {
        var assigned = groups.SelectMany(g => g.PageIndices).ToHashSet();
        if (assigned.Count == 0)
        {
            return [];
        }

        var firstAssigned = assigned.Min();
        return Enumerable.Range(firstAssigned, pageCount - firstAssigned)
            .Where(i => !assigned.Contains(i))
            .Select(i => i + 1)
            .ToList();
    }
}
