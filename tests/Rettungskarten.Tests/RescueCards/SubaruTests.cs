using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Infrastructure.RescueCards.Splitting;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// subaru_page.html is the real "Rettungskarten" paragraph of subaru.de/technik/sicherheit (the current
/// combined PDF plus the invisible empty anchor to the previous edition) and an unrelated brochure link.
/// subaru_sample_pages.pdf is a real 6-page slice of the combined PDF (pages 9, 36, 37, 46, 47, 54 of
/// 57; images replaced by 1x1 pixels to keep it small, text layer untouched): the old-format Justy
/// card, the two Legacy cards with identical headers, the first two pages of the ISO-format BRZ (ZC)
/// card and the Solterra's first page.
/// </summary>
public sealed class SubaruTests
{
    [Fact]
    public async Task DiscoverAsync_TakesOnlyTheVisibleCombinedPdfLink()
    {
        var factory = new StubHttpClientFactory().Html(SubaruRescueCardSource.PageUrl,
            await File.ReadAllTextAsync(Path.Combine("Fixtures", "subaru_page.html")));

        var entries = await new SubaruRescueCardSource(factory, NullLogger<SubaruRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal(DocumentScope.Combined, entry.Scope);
        Assert.Contains("NEU.pdf", entry.RawFileNameOrLabel);
        Assert.Equal(SubaruRescueCardSource.CombinedModelName, entry.Parsed.ModelName);
        Assert.Equal(2025, entry.Parsed.BuildYearTo);
        Assert.Equal("DE", entry.Parsed.LanguageCode);
    }

    [Fact]
    public async Task DownloadAsync_UsesTheLargeDownloadClient()
    {
        const string pdfUrl = "https://www.subaru.de/big.pdf";
        var factory = new StubHttpClientFactory().Bytes(pdfUrl, StubHttpClientFactory.FakePdf());
        var entry = new RescueCardEntry(Brand.Subaru, SubaruRescueCardSource.PageUrl, pdfUrl, "big.pdf",
            new ParsedModelInfo("All Models", null, null, null, null, null, null, "DE", ParseConfidence.Heuristic), DocumentScope.Combined);

        await new SubaruRescueCardSource(factory, NullLogger<SubaruRescueCardSource>.Instance).DownloadAsync(entry, CancellationToken.None);

        Assert.All(factory.Requests, r => Assert.Equal(RettungskartenHttpClient.LargeDownloadName, r.ClientName));
    }

    private static IReadOnlyList<CombinedPdfSplitter.SplitResult> Split() =>
        CombinedPdfSplitter.Split(File.ReadAllBytes(Path.Combine("Fixtures", "subaru_sample_pages.pdf")), new SubaruCombinedPdfLayout());

    [Fact]
    public void Split_GroupsOldIsoAndSolterraCards()
    {
        var results = Split();

        // Justy, Legacy (2 pages, identical headers), BRZ ZC (2 pages, by document id), Solterra.
        Assert.Equal(4, results.Count);
        Assert.Equal(results.Count, results.Select(r => r.DocumentId).Distinct().Count());
    }

    [Fact]
    public void Split_OldFormatCard_ParsesModelTypeAndYears()
    {
        var justy = Assert.Single(Split(), r => r.Parsed.ModelName == "Justy");

        Assert.Equal("M3", justy.Parsed.ChassisCode);
        Assert.Equal(2008, justy.Parsed.BuildYearFrom);
        Assert.Equal(2010, justy.Parsed.BuildYearTo);
    }

    [Fact]
    public void Split_IdenticalConsecutiveHeaders_BecomeOneTwoPagePart()
    {
        var legacy = Assert.Single(Split(), r => r.Parsed.ModelName == "Legacy");

        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(legacy.PdfBytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(2, pdf.PageCount);
        Assert.Equal(2004, legacy.Parsed.BuildYearFrom);
        Assert.Equal(2009, legacy.Parsed.BuildYearTo);
    }

    [Fact]
    public void Split_IsoFormatCard_GroupedByDocumentId()
    {
        var brz = Assert.Single(Split(), r => r.Parsed.ModelName == "BRZ");

        Assert.Equal("JF1-T4477GG", brz.DocumentId);
        Assert.Equal("ZC", brz.Parsed.ChassisCode);
        Assert.Equal(2, brz.Parsed.Doors);
        Assert.Equal(2019, brz.Parsed.BuildYearFrom);
        Assert.Equal(2022, brz.Parsed.BuildYearTo);
        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(brz.PdfBytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(2, pdf.PageCount);
    }

    [Fact]
    public void Split_SolterraHeader_ParsesOpenEndedModelYear()
    {
        var solterra = Assert.Single(Split(), r => r.Parsed.ModelName == "Solterra");

        Assert.Equal(2023, solterra.Parsed.BuildYearFrom);
        Assert.Null(solterra.Parsed.BuildYearTo);
    }
}
