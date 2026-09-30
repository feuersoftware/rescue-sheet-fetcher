using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Infrastructure.RescueCards.Parsing;
using Rettungskarten.Infrastructure.RescueCards.Splitting;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixture kia_page.html is a trimmed copy of the real page: six German-section cards (Picanto -
/// English file only; Sportage - the href with a trailing dot; Sorento Hybrid; EV6 GT - marked
/// "[EN]"; K4; Niro HEV - two language tokens in its filename), the English-section K4 card that
/// duplicates the German one, and the "ältere Modelle" block with the combined PDF.
/// kia_sample_pages.pdf is a 5-page slice of that combined PDF (images replaced by 1x1 placeholders).
///
/// No test here asserts on localized text (only on parsed data), so none sets
/// <c>Strings.OverrideCulture</c>: that process-global switch would only race with the culture tests
/// running in parallel (RunSummaryPrinterTests, StringsTests).
/// </summary>
public sealed class KiaRescueCardSourceTests
{
    private static async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "kia_page.html"));
        var factory = new StubHttpClientFactory().Html(KiaRescueCardSource.PageUrl, html);
        return await new KiaRescueCardSource(factory, NullLogger<KiaRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DiscoverAsync_TakesModelFromCardHeading_AndDropsEnglishDuplicate()
    {
        var entries = await DiscoverAsync();

        Assert.Equal(7, entries.Count);
        var k4 = Assert.Single(entries, e => e.Parsed.ModelName == "K4");
        Assert.Equal("DE", k4.Parsed.LanguageCode);
        Assert.Equal("Hatchback", k4.Parsed.BodyType);
        Assert.Equal(2025, k4.Parsed.BuildYearFrom);
    }

    [Fact]
    public async Task DiscoverAsync_KeepsEnglishOnlySheetsAsEnglish()
    {
        var entries = await DiscoverAsync();

        Assert.Equal("EN", Assert.Single(entries, e => e.Parsed.ModelName == "Picanto").Parsed.LanguageCode);
        var ev6 = Assert.Single(entries, e => e.Parsed.ModelName == "EV6");
        Assert.Equal("EN", ev6.Parsed.LanguageCode);
        Assert.Equal("EV6 GT", ev6.Parsed.Variant);

        // "..._Electric_EN_RVSE_DE.pdf": the German revision of an English original.
        var niro = Assert.Single(entries, e => e.Parsed.ModelName == "Niro");
        Assert.Equal("DE", niro.Parsed.LanguageCode);
        Assert.Equal("HEV", niro.Parsed.FuelType);
    }

    [Fact]
    public async Task DiscoverAsync_KeepsLinkWithTrailingDot_AndMarksCombinedPdf()
    {
        var entries = await DiscoverAsync();

        var sportage = Assert.Single(entries, e => e.Parsed.ModelName == "Sportage");
        Assert.EndsWith("rdb_kia_sportage_2020.pdf.", sportage.DownloadUrl);
        Assert.Equal(2020, sportage.Parsed.BuildYearFrom);

        var combined = Assert.Single(entries, e => e.Scope == DocumentScope.Combined);
        Assert.Equal(KiaRescueCardSource.CombinedModelName, combined.Parsed.ModelName);
    }

    [Theory]
    [InlineData("Ceed SW Plug-in Hybrid", "Ceed", "Sportswagon", "Plug-in Hybrid")]
    [InlineData("Niro EV", "Niro", null, "EV")]
    [InlineData("EV6 GT", "EV6", null, null)]
    [InlineData("PV5 Cargo", "PV5", null, null)]
    [InlineData("cee‘d_sw", "Ceed", "Sportswagon", null)]
    [InlineData("pro_cee‘d", "ProCeed", null, null)]
    [InlineData("Soul EV", "Soul", null, "EV")]
    public void SplitModelName_SeparatesAppendedVariantWords(string name, string model, string? body, string? fuel)
    {
        var parts = KiaRescueSheetParser.SplitModelName(name);

        Assert.Equal(model, parts.ModelName);
        Assert.Equal(body, parts.BodyType);
        Assert.Equal(fuel, parts.FuelType);
    }

    [Fact]
    public void Parse_FindsAttributesAnywhereInIrregularFilename()
    {
        var parsed = KiaRescueSheetParser.Parse("Kia e-Soul", "Kia_e-Soul_5dr-Hatchback_08_2019.pdf");

        Assert.Equal("e-Soul", parsed.ModelName);
        Assert.Equal(5, parsed.Doors);
        Assert.Equal("Hatchback", parsed.BodyType);
        Assert.Equal(2019, parsed.BuildYearFrom);
        Assert.Equal("DE", parsed.LanguageCode);
    }

    [Fact]
    public void Layout_OneSheetPerPage_FromRealSample()
    {
        var results = CombinedPdfSplitter.Split(File.ReadAllBytes(Path.Combine("Fixtures", "kia_sample_pages.pdf")), new KiaCombinedPdfLayout());

        // Notice page skipped; running titles ("Carens Carens", "Ceed sw PHEV Ceed sw PHEV") collapsed.
        Assert.Equal(["Carens", "Ceed", "Ceed", "Soul"], results.Select(r => r.Parsed.ModelName));

        var carens = results[0].Parsed;
        Assert.Equal(2006, carens.BuildYearFrom);
        Assert.Equal(2013, carens.BuildYearTo);
        Assert.Equal("UN", carens.ChassisCode);

        Assert.Equal("Sportswagon", results[1].Parsed.BodyType);
        Assert.Equal("PHEV", results[2].Parsed.FuelType);
        Assert.Equal(2020, results[2].Parsed.BuildYearFrom);
        Assert.Equal("EV", results[3].Parsed.FuelType);
        Assert.Equal(results.Count, results.Select(r => r.DocumentId).Distinct().Count());
    }
}
