using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Regression test against a real downloaded Porsche "Further Documents" page, so a future layout
/// change (the content moving out of the &lt;script type="text/x-template"&gt; block, or the
/// &lt;link-list&gt; structure changing) is caught immediately instead of silently returning nothing.
/// </summary>
public class PorscheDocumentsPageParserTests
{
    [Fact]
    public async Task ParseRescueDataSheetLinksAsync_FindsBothCombinedDocuments()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "porsche_documents_page.html"));

        var links = await PorscheDocumentsPageParser.ParseRescueDataSheetLinksAsync(html, CancellationToken.None);

        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.Text == "Rescue Data Sheets" && l.Href.Contains("Rescue-Data-sheets-2025"));
        Assert.Contains(links, l => l.Text == "Rescue Data Sheets Classic" && l.Href.Contains("Rescue-Data-sheets-Classic"));
        Assert.All(links, l => Assert.StartsWith("https://assets-v2.porsche.com/", l.Href));
    }

    [Fact]
    public async Task ParseRescueDataSheetLinksAsync_UnrelatedHtml_ReturnsEmpty()
    {
        var links = await PorscheDocumentsPageParser.ParseRescueDataSheetLinksAsync(
            "<html><body><p>no template here</p></body></html>", CancellationToken.None);

        Assert.Empty(links);
    }
}
