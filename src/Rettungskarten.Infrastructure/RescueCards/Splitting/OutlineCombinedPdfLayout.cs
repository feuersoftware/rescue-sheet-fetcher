using Rettungskarten.Core.Models;
using UglyToad.PdfPig.Outline;
using PigPdfDocument = UglyToad.PdfPig.PdfDocument;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Base for combined PDFs that have one bookmark per model: each bookmark at
/// <see cref="BookmarkLevel"/> (0 = top level) starts a group that runs until the page before the next
/// bookmark's page, the last one until the end of the document. Bookmarks <see cref="ParseTitle"/>
/// returns null for (table of contents, legend, general notes) are skipped - their pages are dropped,
/// not merged into a neighbour. Two bookmarks pointing at the same page describe the same pages; only
/// the first is kept.
/// </summary>
public abstract class OutlineCombinedPdfLayout : ICombinedPdfLayout
{
    public abstract Brand Brand { get; }

    protected virtual int BookmarkLevel => 0;

    /// <summary>Metadata for one bookmark title, or null if the bookmark isn't a model.</summary>
    protected abstract ParsedModelInfo? ParseTitle(string title);

    public virtual bool IsCombinedEntry(RescueCardMetadata entry) => entry.DocumentScope == DocumentScope.Combined;

    public IReadOnlyList<CombinedPdfPageGroup> DetectGroups(PigPdfDocument document)
    {
        if (!document.TryGetBookmarks(out var bookmarks))
        {
            return [];
        }

        var starts = Flatten(bookmarks.Roots)
            .Where(n => n.Level == BookmarkLevel)
            .OfType<DocumentBookmarkNode>()
            .Where(n => n.PageNumber >= 1 && n.PageNumber <= document.NumberOfPages)
            .OrderBy(n => n.PageNumber)
            .ToList();

        var groups = new List<CombinedPdfPageGroup>();
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedStartPages = new HashSet<int>();
        for (var i = 0; i < starts.Count; i++)
        {
            var title = starts[i].Title.Trim();
            var firstPage = starts[i].PageNumber;
            // Parse before de-duplicating by page: a non-model bookmark (contents, legend) on the same
            // page as a model's must not hide that model.
            var parsed = ParseTitle(title);
            if (parsed is null || !usedStartPages.Add(firstPage))
            {
                continue;
            }

            var nextStart = starts.Skip(i + 1).FirstOrDefault(n => n.PageNumber > firstPage);
            var lastPage = nextStart is not null ? nextStart.PageNumber - 1 : document.NumberOfPages;
            var pageIndices = Enumerable.Range(firstPage - 1, lastPage - firstPage + 1).ToList();

            var key = usedKeys.Add(title) ? title : $"{title}#p{firstPage}";
            groups.Add(new CombinedPdfPageGroup(key, parsed, pageIndices));
        }

        return groups;
    }

    private static IEnumerable<BookmarkNode> Flatten(IEnumerable<BookmarkNode> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));
}
