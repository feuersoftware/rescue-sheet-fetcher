using PdfSharp.Pdf;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Splitting;
using PigPdfDocument = UglyToad.PdfPig.PdfDocument;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Covers the bookmark-based layout base with a synthetic document (blank pages + a PDFsharp outline) -
/// the page-text-based base is covered against a real document by PorscheCombinedPdfLayoutTests.
/// </summary>
public class CombinedPdfSplitterTests
{
    [Fact]
    public void OutlineLayout_EachModelBookmarkStartsAGroupUntilTheNextBookmark()
    {
        // Pages: 1 = contents, 2-3 = Focus, 4 = Kuga, 5-7 = Puma (runs to the end of the document).
        var pdf = BuildPdf(pageCount: 7, ("Inhalt", 1), ("Focus 2018", 2), ("Kuga 2019", 4), ("Puma 2019", 5));

        var results = CombinedPdfSplitter.Split(pdf, new TestOutlineLayout());

        Assert.Equal(["Focus 2018", "Kuga 2019", "Puma 2019"], results.Select(r => r.DocumentId));
        Assert.Equal([2, 1, 3], results.Select(r => PageCount(r.PdfBytes)));
        Assert.Equal("Focus", results[0].Parsed.ModelName);
    }

    [Fact]
    public void OutlineLayout_NoBookmarks_DetectsNothing()
    {
        var pdf = BuildPdf(pageCount: 3);

        Assert.Empty(CombinedPdfSplitter.Split(pdf, new TestOutlineLayout()));
    }

    [Fact]
    public void OutlineLayout_DuplicateTitles_GetUniqueKeys()
    {
        var pdf = BuildPdf(pageCount: 2, ("Ranger", 1), ("Ranger", 2));

        var results = CombinedPdfSplitter.Split(pdf, new TestOutlineLayout());

        Assert.Equal(2, results.Select(r => r.DocumentId).Distinct().Count());
    }

    [Fact]
    public void OutlineLayout_NonModelBookmarkOnTheSamePage_DoesNotHideTheModel()
    {
        var pdf = BuildPdf(pageCount: 3, ("Inhalt", 1), ("Fiesta 2017", 1), ("Kuga 2019", 3));

        var results = CombinedPdfSplitter.Split(pdf, new TestOutlineLayout());

        Assert.Equal(["Fiesta 2017", "Kuga 2019"], results.Select(r => r.DocumentId));
        Assert.Equal([2, 1], results.Select(r => PageCount(r.PdfBytes)));
    }

    private static byte[] BuildPdf(int pageCount, params (string Title, int Page)[] bookmarks)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pageCount; i++)
        {
            document.AddPage();
        }

        foreach (var (title, page) in bookmarks)
        {
            document.Outlines.Add(title, document.Pages[page - 1]);
        }

        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static int PageCount(byte[] pdf)
    {
        using var document = PigPdfDocument.Open(pdf);
        return document.NumberOfPages;
    }

    private sealed class TestOutlineLayout : OutlineCombinedPdfLayout
    {
        public override Brand Brand => Brand.Ford;

        protected override ParsedModelInfo? ParseTitle(string title) =>
            title == "Inhalt"
                ? null
                : new ParsedModelInfo(title.Split(' ')[0], title, null, null, null, null, null, "DE", ParseConfidence.Heuristic);
    }
}
