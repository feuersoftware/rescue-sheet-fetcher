using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Infrastructure.RescueCards.Parsing;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixture (mazda_page.html) is the real "ALLE RETTUNGSKARTEN" FAQ block from mazda.de, trimmed to
/// six models and re-embedded exactly the way the page embeds it (JSON inside a JS single-quoted
/// string literal inside <c>window.mxp.data.push(JSON.parse('…'))</c>), plus an unrelated content
/// block. The kept entries cover the tricky label shapes: a VIN range in its own segment and mixed
/// into the year segment ("2023 ab JMZKF…"), labels in front of a bare "PDF herunterladen" link
/// (CX-60, Mazda6e), the pipe-less "von … bis …" MX-30 labels and English-only sheets.
/// </summary>
public sealed class MazdaRescueCardSourceTests
{
    private const string PageUrl = "https://www.mazda.de/rettungskarten/";

    private static async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "mazda_page.html"));
        var factory = new StubHttpClientFactory().Html(PageUrl, html);
        return await new MazdaRescueCardSource(factory, NullLogger<MazdaRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DiscoverAsync_FindsEveryListedSheet()
    {
        var entries = await DiscoverAsync();

        Assert.Equal(16, entries.Count);
        Assert.All(entries, e => Assert.StartsWith("https://media-assets.mazda.eu/", e.DownloadUrl));
        Assert.All(entries, e => Assert.DoesNotContain("?", e.RawFileNameOrLabel));
        Assert.Equal(entries.Count, entries.Select(e => e.RawFileNameOrLabel).Distinct().Count());
    }

    [Fact]
    public async Task DiscoverAsync_ParsesChassisCodeAndYears_WithoutVinRanges()
    {
        var entries = await DiscoverAsync();

        var ke = Assert.Single(entries, e => e.Parsed.ChassisCode == "KE");
        Assert.Equal("CX-5", ke.Parsed.ModelName);
        Assert.Equal(2012, ke.Parsed.BuildYearFrom);
        Assert.Equal(2017, ke.Parsed.BuildYearTo);
        Assert.DoesNotContain("*", ke.Parsed.Variant);

        // "Mazda CX-5 | KF | 2023 ab JMZKF******350000" - year and VIN range in one segment.
        var kf2023 = Assert.Single(entries, e => e.Parsed.ChassisCode == "KF" && e.Parsed.BuildYearFrom == 2023);
        Assert.Null(kf2023.Parsed.BuildYearTo);
        Assert.Equal("DE", kf2023.Parsed.LanguageCode); // "rsen-khecw-a_de.pdf" is the German edition
    }

    [Fact]
    public async Task DiscoverAsync_ReadsLabelsInFrontOfBarePdfLinks()
    {
        var entries = await DiscoverAsync();

        var cx60 = entries.Where(e => e.Parsed.ModelName == "CX-60").ToList();
        Assert.Equal(2, cx60.Count);
        Assert.All(cx60, e => Assert.Equal("KH", e.Parsed.ChassisCode));
        Assert.Single(cx60, e => e.Parsed.LanguageCode == "EN" && e.Parsed.FuelType == "Mild-Hybrid");

        var mazda6e = Assert.Single(entries, e => e.Parsed.ModelName == "Mazda6e");
        Assert.Equal(2025, mazda6e.Parsed.BuildYearFrom);
        Assert.Null(mazda6e.Parsed.ChassisCode);
    }

    [Fact]
    public async Task DiscoverAsync_HandlesPipelessVonBisLabels()
    {
        var entries = await DiscoverAsync();

        var first = Assert.Single(entries, e => e.Parsed.ModelName == "MX-30" && e.Parsed.BuildYearTo == 2022);
        Assert.Equal(2020, first.Parsed.BuildYearFrom);
        Assert.Equal("DR", first.Parsed.ChassisCode);
    }

    [Fact]
    public async Task DiscoverAsync_KeepsEnglishOnlySheets()
    {
        var entries = await DiscoverAsync();

        var bp2023 = entries.Where(e => e.Parsed.ModelName == "Mazda3" && e.Parsed.BuildYearFrom == 2023).ToList();
        Assert.Equal(2, bp2023.Count);
        Assert.All(bp2023, e => Assert.Equal("EN", e.Parsed.LanguageCode));
        Assert.Equal(2, entries.Count(e => e.Parsed.ModelName == "Mazda3" && e.Parsed.BuildYearFrom == 2019 && e.Parsed.LanguageCode == "DE"));
    }

    [Fact]
    public async Task DiscoverAsync_PageWithoutCardList_ReturnsEmpty()
    {
        var factory = new StubHttpClientFactory().Html(PageUrl, "<html><body><p>Wartung</p></body></html>");

        var entries = await new MazdaRescueCardSource(factory, NullLogger<MazdaRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        Assert.Empty(entries);
    }

    [Fact]
    public async Task DiscoverAsync_PageFailure_Throws()
    {
        var factory = new StubHttpClientFactory().Status(PageUrl, System.Net.HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new MazdaRescueCardSource(factory, NullLogger<MazdaRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("Mazda2", "Mazda2 3-Türer | DE | ab 2007 | JMZDE *****100000 > PDF herunterladen", "mazda2_de_3d_ab2007.pdf", "Mazda2", "DE", 2007, null, 3)]
    [InlineData("Mazda2 Hybrid", " Mazda2 Hybrid 5-Türer | KBAC3 | ab 2024 > PDF herunterladen", "mazda2-hybrid-rettungskarte.pdf", "Mazda2", "KBAC3", 2024, null, 5)]
    [InlineData("Mazda MX-5", "Mazda MX-5 Roadster Coupe | NC (RHT) | ab 12.2012 | JMZNC******350000", "mazda_mx-5_nc_roadster_coupe_fl_ab12_2012.pdf", "MX-5", "NC", 2012, null, null)]
    public void Parser_HandlesRealLabels(string title, string label, string file, string model, string chassis, int from, int? to, int? doors)
    {
        var parsed = MazdaLabelParser.Parse(title, label, file);

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(chassis, parsed.ChassisCode);
        Assert.Equal(from, parsed.BuildYearFrom);
        Assert.Equal(to, parsed.BuildYearTo);
        Assert.Equal(doors, parsed.Doors);
    }

    [Theory]
    [InlineData("mazda_cx-30_en_rsen-tdecw-a.pdf", "EN")]
    [InlineData("mazda_cx-60_rsen-khecw-a.pdf", "EN")]
    [InlineData("rsen-khecw-a_de.pdf", "DE")]
    [InlineData("mazda_cx-80_rsde-klecw-a.pdf", "DE")]
    [InlineData("mazda_cx-5_kf.pdf", "DE")]
    public void DetectLanguage_UsesMazdaDocumentCodes(string file, string expected) =>
        Assert.Equal(expected, MazdaLabelParser.DetectLanguage(file));

    [Fact]
    public void UnescapeJsString_ResolvesOneLevelOfEscaping()
    {
        // As on the page: the JSON's own "\u003c" arrives as "\\u003c", a quote as "\\\"", and a
        // literal apostrophe (which would end the JS literal) as "\u0027". Built from char codes so
        // the C# compiler's own \u escape processing can't interfere.
        const char bs = '\\';
        var literal = "{\"a\":\"" + bs + bs + "u003cb " + bs + bs + bs + "\"x" + bs + bs + bs + "\" it" + bs + "u0027s\"}";

        var unescaped = MazdaRescueCardSource.UnescapeJsString(literal);

        Assert.Equal("{\"a\":\"" + bs + "u003cb " + bs + "\"x" + bs + "\" it's\"}", unescaped);
    }
}
