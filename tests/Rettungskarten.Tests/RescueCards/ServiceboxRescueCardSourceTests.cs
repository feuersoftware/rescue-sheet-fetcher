using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Stellantis;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixtures are trimmed real Servicebox pages (2026-09-29): the DS menu cut down to two model pages,
/// the DS 3 page (row table only), the DS N°8 page (ERG section with "ERG_" files, then a heading
/// with two drivetrain sub-headings), the Peugeot menu cut down to three pages, the 3008 page (an ERG
/// section whose files are NOT named ERG, a row table, six language tables) and the Expert page (rows
/// split over two links, one of them pointing at a Portuguese PDF). Language tables were trimmed to
/// their French/English/German buttons; in the 3008 page the German button of the "3008 Hybrid4"
/// table was removed to exercise the English fallback (every real table has a German one).
///
/// No assertion here reads localized text (only the log lines go through Strings, into a NullLogger),
/// so these tests deliberately leave the process-global Strings.OverrideCulture alone - setting it
/// would race with the culture-asserting tests that run in parallel.
/// </summary>
public sealed class ServiceboxRescueCardSourceTests
{
    private const string DsBase = "https://public.servicebox-parts.com/DS/secours/DS/documents/de_DE/";
    private const string ApBase = "https://public.servicebox-parts.com/AP/secours/AP/documents/de_DE/";
    private const string ApPdf = "https://public.servicebox-parts.com/AP/secours/AP/documents/PDF_FAD/";

    [Fact]
    public async Task DiscoverAsync_Ds_ReadsRowTablesAndSkipsErgSection()
    {
        var factory = new StubHttpClientFactory()
            .Html(DsBase + "indexLocale.html", Fixture("servicebox_ds_menu.html"))
            .Html(DsBase + "AIDE/14716/FAD_DS_DS3.html", Fixture("servicebox_ds_ds3.html"))
            .Html(DsBase + "AIDE/18385/DS_N_8.html", Fixture("servicebox_ds_n8.html"));

        var entries = await new ServiceboxRescueCardSource(Brand.DS, factory, NullLogger<ServiceboxRescueCardSource>.Instance)
            .DiscoverAsync(CancellationToken.None);

        Assert.Equal(5, entries.Count);
        Assert.All(entries, e => Assert.Equal(Brand.DS, e.Brand));
        Assert.All(entries, e => Assert.Equal("DE", e.Parsed.LanguageCode));
        Assert.DoesNotContain(entries, e => e.RawFileNameOrLabel.StartsWith("ERG_", StringComparison.Ordinal));

        var crossback = Assert.Single(entries, e => e.RawFileNameOrLabel == "FAD_DS_3_CROSSBACK_E_TENSE_1SD3_de_DE.pdf");
        Assert.Equal("DS 3 CROSSBACK", crossback.Parsed.ModelName);
        Assert.Equal("1SD3", crossback.Parsed.ChassisCode);
        Assert.Equal(2019, crossback.Parsed.BuildYearFrom);
        Assert.Null(crossback.Parsed.BuildYearTo);
        Assert.Equal("Elektro", crossback.Parsed.FuelType); // the eDS3 - "E-TENSE" without "Hybride"
        Assert.Equal(DsBase + "AIDE/14716/FAD_DS_DS3.html", crossback.SourcePageUrl);
        Assert.Equal("https://public.servicebox-parts.com/DS/secours/DS/documents/PDF_FAD/FAD_DS_3_CROSSBACK_E_TENSE_1SD3_de_DE.pdf", crossback.DownloadUrl);

        // The second drivetrain table only has "AWD (All-Wheel Drive)..." above it - the model and
        // project code are carried over from the N°8 heading.
        var n8 = entries.Where(e => e.Parsed.ChassisCode == "1SQ8").ToList();
        Assert.Equal(2, n8.Count);
        Assert.All(n8, e => Assert.Equal("N°8", e.Parsed.ModelName));
        Assert.All(n8, e => Assert.Equal(2025, e.Parsed.BuildYearFrom));
        Assert.All(n8, e => Assert.Equal("Elektro", e.Parsed.FuelType));
        Assert.Contains(n8, e => e.RawFileNameOrLabel == "DS_N8_AWD_Hatchback_2025_5d_Electric_de_DE.pdf");
    }

    [Fact]
    public async Task DiscoverAsync_Peugeot_HandlesLanguageTablesFallbackAndFailedModelPage()
    {
        var factory = new StubHttpClientFactory()
            .Html(ApBase + "indexLocale.html", Fixture("servicebox_ap_menu.html"))
            .Status(ApBase + "AIDE/14693/FAD_AP_107.html", HttpStatusCode.NotFound)
            .Html(ApBase + "AIDE/14706/FAD_AP_3008.html", Fixture("servicebox_ap_3008.html"))
            .Html(ApBase + "AIDE/14709/FAD_AP_EXPERT.html", Fixture("servicebox_ap_expert.html"));

        var entries = await new ServiceboxRescueCardSource(Brand.Peugeot, factory, NullLogger<ServiceboxRescueCardSource>.Instance)
            .DiscoverAsync(CancellationToken.None);

        // 3008: 3 rows + 6 language tables (the 3 ERG rows are skipped); Expert: 4 rows + 1 table.
        Assert.Equal(14, entries.Count);

        // ERG files under "Handbuch zur Rettung (ERG)" whose names don't say ERG.
        Assert.DoesNotContain(entries, e => e.DownloadUrl!.EndsWith("e3008_(1PPD)_2023_BEV_de_DE.pdf", StringComparison.Ordinal));

        var hybrid4 = Assert.Single(entries, e => e.Parsed.Variant!.StartsWith("3008 Hybrid4", StringComparison.Ordinal));
        Assert.Equal("EN", hybrid4.Parsed.LanguageCode);
        Assert.Equal(ApPdf + "3008_Hybrid4_(1PPD)_2020_en.pdf", hybrid4.DownloadUrl);

        var mhev = Assert.Single(entries, e => e.RawFileNameOrLabel == "3008_MHEV_Hybrid_(1PPD)_2023_de.pdf");
        Assert.Equal("3008", mhev.Parsed.ModelName);
        Assert.Equal("Mild-Hybrid", mhev.Parsed.FuelType);
        Assert.Equal("1PPD", mhev.Parsed.ChassisCode);
        Assert.Equal(2023, mhev.Parsed.BuildYearFrom);

        // Row whose second link points at a Portuguese PDF - the German one is taken.
        var expert = Assert.Single(entries, e => e.Parsed.Variant == "Expert (2PK0) lieferwagen 2016");
        Assert.Equal("FAD_Expert_2PK0_de_DE.pdf", expert.RawFileNameOrLabel);
        Assert.Equal("Kastenwagen", expert.Parsed.BodyType);

        Assert.Equal(entries.Count, entries.Select(e => e.RawFileNameOrLabel).Distinct().Count());
    }

    [Fact]
    public async Task DiscoverAsync_MenuPageFails_Throws()
    {
        var factory = new StubHttpClientFactory().Status(DsBase + "indexLocale.html", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new ServiceboxRescueCardSource(Brand.DS, factory, NullLogger<ServiceboxRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None));
    }

    [Fact]
    public void Constructor_RejectsBrandNotOnServicebox() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceboxRescueCardSource(Brand.Opel, new StubHttpClientFactory(), NullLogger<ServiceboxRescueCardSource>.Instance));

    [Theory]
    [InlineData("Manuel_secours_Ion_1PMS_de_DE.pdf")]
    [InlineData("ERG_DS_N8_BEV_FWD_de_DE.pdf")]
    public void IsRescueSheet_RejectsManualsAndErgFiles(string fileName) =>
        Assert.False(ServiceboxRescueCardSource.IsRescueSheet("Ion (1PMS) 2011→", fileName));

    [Theory]
    [InlineData("../../../PDF_FAD/FAD_208_1PP2_de_DE.pdf", "DE")]
    [InlineData("../../../PDF_FAD/e308_(1PP5)_2023_de.pdf", "DE")]
    [InlineData("../../../PDF_FAD/FAD_3008_en_GB.pdf", "EN")]
    [InlineData("../../../PDF_FAD/FAD_C5_fr.pdf", "FR")]
    [InlineData("../../../PDF_FAD/FAD_208_1PIA.pdf", "DE")] // no suffix: the page's own locale
    // Regression: matching ignored case, so a trailing upper-case body/trim token was read as a
    // language and the row was dropped as neither German nor English.
    [InlineData("../../../PDF_FAD/FAD_308_SW.pdf", "DE")]
    [InlineData("../../../PDF_FAD/e208_GT.pdf", "DE")]
    [InlineData("../../../PDF_FAD/FAD_C3_1CSC_2024_5d_GD.pdf", "DE")]
    public void LanguageOf_ReadsOnlyRealLocaleSuffixes(string fileUrl, string expected) =>
        Assert.Equal(expected, ServiceboxModelPageParser.LanguageOf(fileUrl));

    private static string Fixture(string name) => File.ReadAllText(Path.Combine("Fixtures", name));
}
