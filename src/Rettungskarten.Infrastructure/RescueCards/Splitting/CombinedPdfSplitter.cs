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
    /// free-text field of <paramref name="Parsed"/>.</summary>
    public sealed record SplitResult(ParsedModelInfo Parsed, string DocumentId, byte[] PdfBytes);

    public static IReadOnlyList<SplitResult> Split(byte[] combinedPdfBytes, ICombinedPdfLayout layout)
    {
        IReadOnlyList<CombinedPdfPageGroup> groups;
        using (var document = PigPdfDocument.Open(combinedPdfBytes))
        {
            groups = layout.DetectGroups(document);
        }

        if (groups.Count == 0)
        {
            return [];
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
            results.Add(new SplitResult(group.Parsed, group.Key, outputStream.ToArray()));
        }

        return results;
    }
}
