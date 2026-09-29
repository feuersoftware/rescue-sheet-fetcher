using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Infrastructure.RescueCards.Parsing;
using Rettungskarten.Infrastructure.RescueCards.Splitting;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixture nissan_page.html is a trimmed copy of the real page: the QASHQAI table (an ERG linked next
/// to a sheet, filenames without years such as "Nissan_Qashqai_J10.pdf"), the LEAF table (one href
/// relative without a leading slash) and the "Alle Modelle" table with the combined PDF.
/// nissan_sample_pages.pdf is an 8-page slice of the combined PDF (images replaced by 1x1
/// placeholders): e-NV200 with three header-less high-voltage pages, LEAF with its header repeated on
/// page two, "NAVARA NAVARA King Cab" and "QASHQAI+2".
///
/// No test here asserts on localized text (only on parsed data), so none sets
/// <c>Strings.OverrideCulture</c>: that process-global switch would only race with the culture tests
/// running in parallel (RunSummaryPrinterTests, StringsTests).
/// </summary>
public sealed class NissanRescueCardSourceTests
{
    private static async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "nissan_page.html"));
        var factory = new StubHttpClientFactory().Html(NissanRescueCardSource.PageUrl, html);
        return await new NissanRescueCardSource(factory, NullLogger<NissanRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DiscoverAsync_ReadsEveryTableRow_WithoutErg()
    {
        var entries = await DiscoverAsync();

        Assert.Equal(9, entries.Count);
        Assert.DoesNotContain(entries, e => e.DownloadUrl!.Contains("_ERG", StringComparison.OrdinalIgnoreCase));
        Assert.Single(entries, e => e.Scope == DocumentScope.Combined);
        Assert.All(entries, e => Assert.Equal("DE", e.Parsed.LanguageCode));
    }

    [Fact]
    public async Task DiscoverAsync_TakesYearsAndChassisCodeFromTheRow()
    {
        var entries = await DiscoverAsync();

        var j10 = Assert.Single(entries, e => e.DownloadUrl!.EndsWith("Nissan_Qashqai_J10.pdf"));
        Assert.Equal("Qashqai", j10.Parsed.ModelName);
        Assert.Equal("J10", j10.Parsed.ChassisCode);
        Assert.Equal(2007, j10.Parsed.BuildYearFrom);
        Assert.Equal(2014, j10.Parsed.BuildYearTo);

        var ePower = Assert.Single(entries, e => e.Parsed.ModelName == "Qashqai" && e.Parsed.BuildYearFrom == 2022);
        Assert.Equal("J12", ePower.Parsed.ChassisCode);
        Assert.Equal(5, ePower.Parsed.Doors);
        Assert.Equal("SUV", ePower.Parsed.BodyType);
        Assert.StartsWith("e-Power", ePower.Parsed.Variant);
    }

    [Fact]
    public async Task DiscoverAsync_ResolvesRelativeHrefWithoutLeadingSlash()
    {
        var entries = await DiscoverAsync();

        var leaf2026 = Assert.Single(entries, e => e.Parsed.ModelName == "Leaf" && e.Parsed.BuildYearFrom == 2026);
        Assert.Equal(
            "https://www.nissan.de/content/dam/Nissan/nissan_europe/TDIEU_MY_Rescuers_page/Rescue_PDF/DE/Leaf/Nissan_Leaf_RS_SUV_2026_5d_EV_DE.pdf",
            leaf2026.DownloadUrl);
        Assert.Equal("ZE2", leaf2026.Parsed.ChassisCode);
    }

    [Theory]
    [InlineData("J12, 5-Türer, SUV", "J12")]
    [InlineData("Qashqai+2 J10", "J10")]
    [InlineData("D23 Crew Cab", "D23")]
    [InlineData("L1 W", null)]
    [InlineData("EXDD", "EXDD")]
    public void ExtractChassisCode_TakesThePlatformCodeOnly(string version, string? expected) =>
        Assert.Equal(expected, NissanRescueTableRowParser.ExtractChassisCode(version));

    [Fact]
    public void Layout_GroupsContinuationPages_FromRealSample()
    {
        var results = CombinedPdfSplitter.Split(File.ReadAllBytes(Path.Combine("Fixtures", "nissan_sample_pages.pdf")), new NissanCombinedPdfLayout());

        Assert.Equal(["e-NV200", "LEAF", "NAVARA", "QASHQAI+2"], results.Select(r => r.Parsed.ModelName));
        Assert.Equal([4, 2, 1, 1], results.Select(r => PageCount(r.PdfBytes)));

        var navara = results[2].Parsed;
        Assert.Equal("King Cab", navara.BodyType);
        Assert.Equal("D40", navara.ChassisCode);
        Assert.Equal(2005, navara.BuildYearFrom);
        Assert.Equal(2015, navara.BuildYearTo);
        Assert.Null(results[0].Parsed.BuildYearTo);
    }

    private static int PageCount(byte[] pdf)
    {
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        return document.PageCount;
    }
}
