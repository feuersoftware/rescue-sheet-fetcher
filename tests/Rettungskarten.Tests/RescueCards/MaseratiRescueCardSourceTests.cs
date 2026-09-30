using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Splitting;
using Rettungskarten.Infrastructure.RescueCards.Stellantis;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// maserati_aftersales_page.html is the real download section of maserati.com's German aftersales
/// page, with the rescue-sheet link (an icon button without text) and an unrelated insurance PDF next
/// to it. maserati_sample_pages.pdf is pages 5-6 of the real combined PDF (copied with PDFsharp): the
/// two Quattroporte generations, which share the model name and must still become two parts.
/// </summary>
public sealed class MaseratiRescueCardSourceTests : IDisposable
{
    private const string PageUrl = "https://www.maserati.com/de/de/shopping-tools/aftersales-services";

    public MaseratiRescueCardSourceTests() => Strings.OverrideCulture = new CultureInfo("en");

    public void Dispose() => Strings.OverrideCulture = null;

    [Fact]
    public async Task DiscoverAsync_FindsOnlyTheCombinedRescueSheetPdf()
    {
        var factory = new StubHttpClientFactory()
            .Html(PageUrl, await File.ReadAllTextAsync(Path.Combine("Fixtures", "maserati_aftersales_page.html")));
        var source = new MaseratiRescueCardSource(factory, NullLogger<MaseratiRescueCardSource>.Instance);

        var entries = await source.DiscoverAsync(CancellationToken.None);

        var entry = Assert.Single(entries); // not Maserati_Police_DE.pdf
        Assert.Equal(DocumentScope.Combined, entry.Scope);
        Assert.Equal(MaseratiRescueCardSource.CombinedModelName, entry.Parsed.ModelName);
        Assert.Equal("DE", entry.Parsed.LanguageCode);
        Assert.Equal(
            "https://www.maserati.com/content/dam/maserati/regional/de/Preislisten/Minibooks_MY_2020_Range/original/Maserati_Rettungsdatenblatter_5_2016.pdf",
            entry.DownloadUrl);
        Assert.All(factory.Requests, r => Assert.Equal(RettungskartenHttpClient.BrowserName, r.ClientName));
    }

    [Fact]
    public void Split_OnePartPerPage_KeyedByModelAndStartYear()
    {
        var results = CombinedPdfSplitter.Split(
            File.ReadAllBytes(Path.Combine("Fixtures", "maserati_sample_pages.pdf")), new MaseratiCombinedPdfLayout());

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("Quattroporte", r.Parsed.ModelName));
        Assert.Equal(results.Count, results.Select(r => r.DocumentId).Distinct().Count());

        var old = Assert.Single(results, r => r.Parsed.BuildYearFrom == 2004);
        Assert.Equal(2012, old.Parsed.BuildYearTo);
        var current = Assert.Single(results, r => r.Parsed.BuildYearFrom == 2013);
        Assert.Null(current.Parsed.BuildYearTo);
        Assert.All(results, r => Assert.Equal("%PDF"u8.ToArray(), r.PdfBytes[..4]));
    }

    [Fact]
    public void CombinedPdfLayouts_KnowsMaserati() =>
        Assert.Single(CombinedPdfLayouts.For(Brand.Maserati));
}
