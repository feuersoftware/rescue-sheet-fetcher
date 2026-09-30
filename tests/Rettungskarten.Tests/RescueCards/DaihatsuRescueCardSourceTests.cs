using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Infrastructure.RescueCards.Splitting;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

public sealed class DaihatsuRescueCardSourceTests
{
    [Fact]
    public async Task DiscoverAsync_FindsTheCombinedPdfOnTheHomepage()
    {
        // Real daihatsu.de homepage (2026-09-29); it also links an unrelated CO2 consumer-info PDF.
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "daihatsu_home.html"));
        var factory = new StubHttpClientFactory().Html("https://www.daihatsu.de/", html);
        var source = new DaihatsuRescueCardSource(factory, NullLogger<DaihatsuRescueCardSource>.Instance);

        var entries = await source.DiscoverAsync(CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal("https://www.daihatsu.de/daihatsu_rettungsdatenblaetter_de_at.pdf", entry.DownloadUrl);
        Assert.Equal(DocumentScope.Combined, entry.Scope);
        Assert.Equal("All Models", entry.Parsed.ModelName);
        Assert.Equal("DE", entry.Parsed.LanguageCode);
    }
}

/// <summary>
/// Fixture (daihatsu_sample_pages.pdf, ~240KB) is built from the real 25-page, 5.4MB document: page 2
/// (the overview table the layout reads) and page 7 (the Charade NSP90 sheet) are the real pages at
/// their original positions; pages 1 and 3-6 are blank placeholders for the (image-only, ~220KB each)
/// cover and sheets, so the table's page numbers still point at the right pages. The table's link
/// annotations were stripped first - they reference every page of the original and would otherwise
/// drag the whole document into the copy. Rows for pages 8-25 lie beyond the fixture and must be
/// ignored.
/// </summary>
public class DaihatsuCombinedPdfLayoutTests
{
    private static byte[] LoadFixture() => File.ReadAllBytes(Path.Combine("Fixtures", "daihatsu_sample_pages.pdf"));

    [Fact]
    public void ReadOverviewTable_ReadsAllRowsOfTheRealTable()
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(LoadFixture());

        var rows = DaihatsuCombinedPdfLayout.ReadOverviewTable(document);

        Assert.Equal(23, rows.Count);
        Assert.Equal(Enumerable.Range(3, 23), rows.Select(r => r.Page));
        // Rows without a Modell cell continue the model above.
        Assert.Equal(["CHARADE", "CHARADE", "CHARADE", "CHARADE"], rows.Where(r => r.Page is >= 4 and <= 7).Select(r => r.Model));
        Assert.Equal("GRAN MOVE", Assert.Single(rows, r => r.Page == 17).Model);
    }

    [Fact]
    public void Split_OnePartPerTableRowWithinTheDocument()
    {
        var results = CombinedPdfSplitter.Split(LoadFixture(), new DaihatsuCombinedPdfLayout());

        Assert.Equal(5, results.Count); // pages 3-7
        Assert.Equal(results.Count, results.Select(r => r.DocumentId).Distinct().Count());
        Assert.All(results, r =>
        {
            Assert.Equal("%PDF"u8.ToArray(), r.PdfBytes[..4]);
            Assert.Equal("DE", r.Parsed.LanguageCode);
        });
    }

    [Fact]
    public void Split_ParsesTableColumns()
    {
        var results = CombinedPdfSplitter.Split(LoadFixture(), new DaihatsuCombinedPdfLayout());

        var applause = Assert.Single(results, r => r.Parsed.ModelName == "Applause");
        Assert.Equal(1998, applause.Parsed.BuildYearFrom);
        Assert.Equal(2001, applause.Parsed.BuildYearTo);
        Assert.Equal("A1", applause.Parsed.ChassisCode);

        var charade4 = Assert.Single(results, r => r.Parsed.Doors == 4);
        Assert.Equal("Charade", charade4.Parsed.ModelName);
        Assert.Equal("G204 4-Türer", charade4.Parsed.Variant); // "./." placeholder removed

        // "03/2011" - single date is the start of production.
        var charade2011 = Assert.Single(results, r => r.Parsed.BuildYearFrom == 2011);
        Assert.Null(charade2011.Parsed.BuildYearTo);
        Assert.Equal("NSP 90", charade2011.Parsed.ChassisCode);
    }

    [Fact]
    public void Split_LastPartRunsToTheEndOfTheDocument_EachSheetOnePage()
    {
        var results = CombinedPdfSplitter.Split(LoadFixture(), new DaihatsuCombinedPdfLayout());

        Assert.All(results, r =>
        {
            using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(r.PdfBytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            Assert.Equal(1, pdf.PageCount);
        });
    }

    [Fact]
    public void Layout_IsRegisteredForDaihatsu() =>
        Assert.Single(CombinedPdfLayouts.For(Brand.Daihatsu));
}
