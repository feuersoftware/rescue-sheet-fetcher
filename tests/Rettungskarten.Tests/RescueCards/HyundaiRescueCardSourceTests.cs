using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Infrastructure.RescueCards.Parsing;
using Rettungskarten.Infrastructure.RescueCards.Splitting;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixture hyundai_page.html is a trimmed copy of the real page: three rescue-sheet items (Scene7
/// links without a .pdf extension), the combined "vor 11/2019" PDF, one "Maßnahmen im Notfall" ERG
/// (whose label also says "Rettungsdatenblatt") and one AVN manual. hyundai_sample_pages.pdf is a
/// 9-page slice of the real combined PDF (images replaced by 1x1 placeholders to keep it small; the
/// layout only reads text): a contents page, two single-page sheets, the five-page IONIQ Elektro sheet
/// whose later pages carry no header, and "(Grand) Santa Fe".
///
/// No test here asserts on localized text (only on parsed data), so none sets
/// <c>Strings.OverrideCulture</c>: that process-global switch would only race with the culture tests
/// running in parallel (RunSummaryPrinterTests, StringsTests).
/// </summary>
public sealed class HyundaiRescueCardSourceTests
{
    private static async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "hyundai_page.html"));
        var factory = new StubHttpClientFactory().Html(HyundaiRescueCardSource.PageUrl, html);
        return await new HyundaiRescueCardSource(factory, NullLogger<HyundaiRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DiscoverAsync_KeepsRescueSheetsAndCombinedPdf_DropsErgAndManual()
    {
        var entries = await DiscoverAsync();

        Assert.Equal(4, entries.Count);
        Assert.DoesNotContain(entries, e => e.DownloadUrl!.Contains("Massnahmen", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, e => e.DownloadUrl!.Contains("AVN", StringComparison.OrdinalIgnoreCase));
        Assert.All(entries, e => Assert.Equal("DE", e.Parsed.LanguageCode));
    }

    [Fact]
    public async Task DiscoverAsync_ParsesModelAndVariantFromLabel()
    {
        var entries = await DiscoverAsync();

        var bayon = Assert.Single(entries, e => e.Parsed.ModelName == "BAYON");
        Assert.Equal("48V-Hybrid", bayon.Parsed.FuelType);
        Assert.Equal(2021, bayon.Parsed.BuildYearFrom);
        Assert.Equal("https://dmassets.hyundai.com/is/content/hyundaiautoever/hyundai-bayon-48V-hybrid-rettungsdatenblattpdf", bayon.DownloadUrl);

        var ioniq = Assert.Single(entries, e => e.Parsed.ModelName == "IONIQ 5");
        Assert.Equal("IONIQ 5 N", ioniq.Parsed.Variant);
        Assert.Equal(2024, ioniq.Parsed.BuildYearFrom);
    }

    [Fact]
    public async Task DiscoverAsync_MarksCombinedPdf()
    {
        var entries = await DiscoverAsync();

        var combined = Assert.Single(entries, e => e.Scope == DocumentScope.Combined);
        Assert.Equal(HyundaiRescueCardSource.CombinedModelName, combined.Parsed.ModelName);
        Assert.Equal(2019, combined.Parsed.BuildYearTo);
    }

    [Theory]
    [InlineData("KONA Hybrid", "KONA", null, "Hybrid")]
    [InlineData("SANTA FE Plug-in-Hybrid", "SANTA FE", null, "Plug-in-Hybrid")]
    [InlineData("i30 Kombi", "i30", "Kombi", null)]
    [InlineData("i30cw", "i30", "Kombi", null)]
    [InlineData("(Grand) Santa Fe", "Santa Fe", null, null)]
    [InlineData("Accent, 3-Türer", "Accent", null, null)]
    [InlineData("ix35 FCEV", "ix35", null, "FCEV")]
    [InlineData("Coupe, 2-Türer", "Coupe", null, null)]
    public void SplitModelName_SeparatesAppendedVariantWords(string name, string model, string? body, string? fuel)
    {
        var parts = HyundaiLabelParser.SplitModelName(name);

        Assert.Equal(model, parts.ModelName);
        Assert.Equal(body, parts.BodyType);
        Assert.Equal(fuel, parts.FuelType);
    }

    [Theory]
    // Real page texts of the combined PDF, in PdfPig's order.
    [InlineData("i30 Stand 05/2010 nur i30 LPG i30, 5-Türer (Typ FD/FDH, 2008-2012) ", "i30, 5-Türer", "FD/FDH, 2008-2012")]
    [InlineData("Atos Stand 06/2011 Atos (Typ MX, MXL, MXI 1998-2008) ", "Atos", "MX, MXL, MXI 1998-2008")]
    [InlineData("Stand 04/2020 4/5 Methode 2: 1.Dargestellte Sicherungen entfernen. Nexo FCEV (Typ FE, ab 2018) Fahrzeug", "Nexo FCEV", "FE, ab 2018")]
    public void Layout_FindHeader_IgnoresRunningTitleAndNotes(string pageText, string name, string typ)
    {
        var header = HyundaiCombinedPdfLayout.FindHeader(pageText);

        Assert.Equal((name, typ), header);
    }

    [Fact]
    public void Layout_GroupsSheetsFromRealSample()
    {
        var results = CombinedPdfSplitter.Split(File.ReadAllBytes(Path.Combine("Fixtures", "hyundai_sample_pages.pdf")), new HyundaiCombinedPdfLayout());

        // Accent, Atos, IONIQ Elektro (5 pages), (Grand) Santa Fe - the contents page is skipped.
        Assert.Equal(["Accent", "Atos", "Ioniq", "Santa Fe"], results.Select(r => r.Parsed.ModelName));

        var accent = results[0].Parsed;
        Assert.Equal(3, accent.Doors);
        Assert.Equal(2000, accent.BuildYearFrom);
        Assert.Equal(2006, accent.BuildYearTo);
        Assert.Equal("LC", accent.ChassisCode);

        var ioniq = results[2];
        Assert.Equal("Elektro", ioniq.Parsed.FuelType);
        Assert.Equal(2019, ioniq.Parsed.BuildYearFrom);
        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(ioniq.PdfBytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(5, pdf.PageCount);

        Assert.Equal(1998, results[1].Parsed.BuildYearFrom);
        Assert.Equal("MX", results[1].Parsed.ChassisCode);
        Assert.Equal(results.Count, results.Select(r => r.DocumentId).Distinct().Count());
    }
}
