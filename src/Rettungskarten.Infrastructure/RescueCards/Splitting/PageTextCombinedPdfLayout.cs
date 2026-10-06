using Rettungskarten.Core.Models;
using PigPdfDocument = UglyToad.PdfPig.PdfDocument;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Base for combined PDFs without usable bookmarks, where every page (or every model's first page)
/// identifies its model in its own text - e.g. Porsche's "ID no. ..." footer on every page, or a
/// "model / from year" header on each model's first page.
///
/// <see cref="TryGetPageKey"/> returns a page's group key (or null). Pages without a key either join
/// the preceding group (<see cref="UnkeyedPagesContinuePreviousGroup"/>, for layouts where only a
/// model's first page carries the header) or are skipped (cover/legal-notice/legend pages in layouts
/// where every model page is keyed). Both outcomes are reported back - joined pages as the group's
/// <see cref="CombinedPdfPageGroup.HeaderlessPageIndices"/>, skipped ones as pages no group covers -
/// because both are also exactly what a model whose header stops matching after a document update
/// looks like. With <see cref="MergeNonConsecutivePagesWithSameKey"/> (the
/// default) a key seen again later appends to its first group; without it, it starts a new group under
/// a suffixed key.
/// </summary>
public abstract class PageTextCombinedPdfLayout : ICombinedPdfLayout
{
    public abstract Brand Brand { get; }

    protected virtual bool UnkeyedPagesContinuePreviousGroup => false;

    protected virtual bool MergeNonConsecutivePagesWithSameKey => true;

    /// <summary>The page's group key, or null if the page doesn't identify a model by itself.</summary>
    protected abstract string? TryGetPageKey(string pageText);

    /// <summary>Metadata for one group, given the text of all of its pages in order (the first page
    /// usually carries the header).</summary>
    protected abstract ParsedModelInfo ParseGroup(string key, IReadOnlyList<string> pageTexts);

    public virtual bool IsCombinedEntry(RescueCardMetadata entry) => entry.DocumentScope == DocumentScope.Combined;

    public IReadOnlyList<CombinedPdfPageGroup> DetectGroups(PigPdfDocument document)
    {
        var order = new List<string>();
        var pagesByKey = new Dictionary<string, List<(int Index, string Text)>>();
        var headerlessByKey = new Dictionary<string, List<int>>();
        string? previousKey = null;
        string? previousRawKey = null;
        var runCounter = 0;

        foreach (var page in document.GetPages())
        {
            var text = page.Text;
            var rawKey = TryGetPageKey(text);
            var key = rawKey;

            if (key is null)
            {
                if (!UnkeyedPagesContinuePreviousGroup || previousKey is null)
                {
                    continue;
                }

                key = previousKey;
                if (!headerlessByKey.TryGetValue(key, out var headerless))
                {
                    headerless = [];
                    headerlessByKey[key] = headerless;
                }

                headerless.Add(page.Number - 1);
            }
            else if (!MergeNonConsecutivePagesWithSameKey && rawKey == previousRawKey)
            {
                // Still the same run - keep the (possibly suffixed) key of the run it started.
                key = previousKey!;
            }
            else if (!MergeNonConsecutivePagesWithSameKey && pagesByKey.ContainsKey(key))
            {
                // Same key again after another model's pages: a distinct group that still needs a
                // unique key.
                key = $"{key}#{++runCounter}";
            }

            if (!pagesByKey.TryGetValue(key, out var pages))
            {
                pages = [];
                pagesByKey[key] = pages;
                order.Add(key);
            }

            pages.Add((page.Number - 1, text)); // PdfPig is 1-based, PDFsharp page indices are 0-based
            previousKey = key;
            previousRawKey = rawKey ?? previousRawKey;
        }

        return order
            .Select(k => new CombinedPdfPageGroup(
                k,
                ParseGroup(k, pagesByKey[k].Select(p => p.Text).ToList()),
                pagesByKey[k].Select(p => p.Index).ToList(),
                headerlessByKey.GetValueOrDefault(k)))
            .ToList();
    }
}
