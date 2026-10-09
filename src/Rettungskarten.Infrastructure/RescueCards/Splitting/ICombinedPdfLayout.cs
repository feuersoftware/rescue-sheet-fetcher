using Rettungskarten.Core.Models;
using PigPdfDocument = UglyToad.PdfPig.PdfDocument;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>One model's pages inside a combined PDF. <paramref name="Key"/> must be unique within one
/// document and stable across runs (it feeds the split part's persisted id) - a document-internal id
/// or a printed type code, never a free-text header that two models could share a prefix of.
/// <paramref name="PageIndices"/> are 0-based. <paramref name="HeaderlessPageIndices"/> are the pages
/// among them that didn't identify the model themselves and were assigned only because they follow
/// one that did - reported by <c>split</c> so a model whose header wasn't recognized can't silently
/// end up inside the part of the model before it. <paramref name="PageNumberingMismatch"/> is set when
/// the pages' own numbering ("Page x of y") says the group is missing a page or holds a foreign one -
/// also reported by <c>split</c>.</summary>
public sealed record CombinedPdfPageGroup(
    string Key, ParsedModelInfo Parsed, IReadOnlyList<int> PageIndices, IReadOnlyList<int>? HeaderlessPageIndices = null,
    bool PageNumberingMismatch = false);

/// <summary>
/// How one brand's combined "all models" PDF is laid out: which stored entries are such documents and
/// how their pages map to models. <see cref="CombinedPdfSplitter"/> does the brand-independent part
/// (reading the file, copying page ranges into standalone PDFs). <see cref="PageTextCombinedPdfLayout"/>
/// is the reusable base for the common case: models recognizable from each page's own text.
/// </summary>
public interface ICombinedPdfLayout
{
    Brand Brand { get; }

    /// <summary>Whether a stored entry is one of this brand's combined documents - normally any entry
    /// the brand source marked <see cref="DocumentScope.Combined"/>.</summary>
    bool IsCombinedEntry(RescueCardMetadata entry);

    IReadOnlyList<CombinedPdfPageGroup> DetectGroups(PigPdfDocument document);
}
