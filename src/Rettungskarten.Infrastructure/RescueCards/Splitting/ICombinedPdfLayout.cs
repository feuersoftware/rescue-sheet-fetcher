using Rettungskarten.Core.Models;
using PigPdfDocument = UglyToad.PdfPig.PdfDocument;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>One model's pages inside a combined PDF. <paramref name="Key"/> must be unique within one
/// document and stable across runs (it feeds the split part's persisted id) - a document-internal id
/// or bookmark title, never a free-text header that two models could share a prefix of.
/// <paramref name="PageIndices"/> are 0-based.</summary>
public sealed record CombinedPdfPageGroup(string Key, ParsedModelInfo Parsed, IReadOnlyList<int> PageIndices);

/// <summary>
/// How one brand's combined "all models" PDF is laid out: which stored entries are such documents and
/// how their pages map to models. <see cref="CombinedPdfSplitter"/> does the brand-independent part
/// (reading the file, copying page ranges into standalone PDFs). Two reusable bases cover the layouts
/// seen so far: <see cref="OutlineCombinedPdfLayout"/> (the PDF has one bookmark per model) and
/// <see cref="PageTextCombinedPdfLayout"/> (models are recognizable from each page's own text).
/// </summary>
public interface ICombinedPdfLayout
{
    Brand Brand { get; }

    /// <summary>Whether a stored entry is one of this brand's combined documents - normally any entry
    /// the brand source marked <see cref="DocumentScope.Combined"/>.</summary>
    bool IsCombinedEntry(RescueCardMetadata entry);

    IReadOnlyList<CombinedPdfPageGroup> DetectGroups(PigPdfDocument document);
}
