using Rettungskarten.Infrastructure.RescueCards.Splitting;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Regression test against a real 16-page slice of Porsche's combined PDF (extracted with pikepdf so
/// unreferenced shared resources are dropped - a plain per-page split of the original balloons to the
/// size of the whole 55MB document, since it doesn't garbage-collect shared objects). The slice covers
/// six real models: four single-page ones and two that span multiple physical pages (5 and 6 pages),
/// so both grouping shapes are exercised against real content, not a synthetic approximation.
/// </summary>
public class PorscheCombinedPdfLayoutTests
{
    private static byte[] LoadFixture() => File.ReadAllBytes(Path.Combine("Fixtures", "porsche_sample_pages.pdf"));

    [Fact]
    public void Split_GroupsPagesByModelId_SixModelsFromSixteenPages()
    {
        var results = CombinedPdfSplitter.Split(LoadFixture(), new PorscheCombinedPdfLayout());

        Assert.Equal(6, results.Count);
    }

    [Fact]
    public void FindUnassignedPages_ListsOnlyGapsAfterTheFirstAssignedPage()
    {
        // 6 pages, parts cover pages 2, 3 and 5 (0-based 1, 2, 4): page 1 is leading furniture (cover,
        // legal notice) and expected; pages 4 and 6 are not - something there matched no model.
        var parsed = new Rettungskarten.Core.Models.ParsedModelInfo("X", null, null, null, null, null, null, "DE", Rettungskarten.Core.Models.ParseConfidence.Heuristic);
        var groups = new[] { new CombinedPdfPageGroup("a", parsed, [1, 2]), new CombinedPdfPageGroup("b", parsed, [4]) };

        Assert.Equal([4, 6], CombinedPdfSplitter.FindUnassignedPages(groups, pageCount: 6));
    }

    [Fact]
    public void Split_SinglePageModel_ExtractsNameAndYearRange()
    {
        var results = CombinedPdfSplitter.Split(LoadFixture(), new PorscheCombinedPdfLayout());

        var cayenne2003 = Assert.Single(results, r => r.Parsed.BuildYearFrom == 2003);
        Assert.Equal("Cayenne", cayenne2003.Parsed.ModelName);
        Assert.Equal(2005, cayenne2003.Parsed.BuildYearTo);
        Assert.Equal("SUV", cayenne2003.Parsed.BodyType);
        Assert.Equal("EN", cayenne2003.Parsed.LanguageCode);
    }

    [Fact]
    public void Split_OpenEndedYearRange_ParsesFromModelYearOnly()
    {
        var results = CombinedPdfSplitter.Split(LoadFixture(), new PorscheCombinedPdfLayout());

        var hybrid = Assert.Single(results, r => r.Parsed.Variant != null && r.Parsed.Variant.StartsWith("Cayenne S Hybrid (92A)"));
        Assert.Equal(2011, hybrid.Parsed.BuildYearFrom);
        Assert.Null(hybrid.Parsed.BuildYearTo);
    }

    [Fact]
    public void Split_MultiPageModel_CombinesAllPagesIntoOnePdf()
    {
        var results = CombinedPdfSplitter.Split(LoadFixture(), new PorscheCombinedPdfLayout());

        // "Cayenne S Hybrid (92A)" spans 5 physical pages (Page 1 of 5 .. Page 5 of 5) - all of them
        // must end up as one 5-page output PDF, not five separate single-page ones.
        var fivePageModel = Assert.Single(results, r => r.Parsed.Variant != null && r.Parsed.Variant.StartsWith("Cayenne S Hybrid"));

        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(fivePageModel.PdfBytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(5, pdf.PageCount);
    }

    [Fact]
    public void Split_EveryResult_ProducesValidPdfBytes()
    {
        var results = CombinedPdfSplitter.Split(LoadFixture(), new PorscheCombinedPdfLayout());

        Assert.All(results, r =>
        {
            Assert.True(r.PdfBytes.Length > 0);
            Assert.Equal("%PDF"u8.ToArray(), r.PdfBytes[..4]);
        });
    }

    [Fact]
    public void Split_DocumentIdIsUniquePerGroup()
    {
        // DocumentId (not the length-capped Parsed.Variant) is what callers must use to build a
        // stable/collision-resistant persisted id - two distinct models can share a long enough
        // common name/body-type prefix that Variant's cap is reached before either model's
        // disambiguating "ID no." text differs, but DocumentId is the grouping key itself and is
        // therefore guaranteed distinct for every returned result.
        var results = CombinedPdfSplitter.Split(LoadFixture(), new PorscheCombinedPdfLayout());

        var distinctIds = results.Select(r => r.DocumentId).Distinct().ToList();
        Assert.Equal(results.Count, distinctIds.Count);
        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.DocumentId)));
    }

    [Fact]
    public void Split_LeadingLegalNoticePage_ProducesNoSpuriousEntry()
    {
        // The fixture's first page is a legal-notice page with no "ID no." footer at all - it must be
        // silently skipped, not turned into a crash or a garbage entry with no model info.
        var results = CombinedPdfSplitter.Split(LoadFixture(), new PorscheCombinedPdfLayout());

        Assert.DoesNotContain(results, r => r.Parsed.ModelName is null && r.Parsed.Variant is null);
    }

    [Theory]
    [InlineData("Boxter/S/Spyder (987) Cabriolet", "Boxster")]
    [InlineData("Boxter Spyder (981) Cabriolet", "Boxster Spyder")]
    [InlineData("Boxter/S/GTS (981) Cabriolet", "Boxster")]
    [InlineData("911 Carrera (997) Coupe", "911 Carrera")]
    public void ExtractModelName_CorrectsKnownBoxterTypo(string headerText, string expectedModelName)
    {
        // Porsche's own combined PDF genuinely misspells "Boxster" as "Boxter" in several places -
        // left uncorrected, this splits one real model across "boxster"/"boxter"/"boxter-spyder"
        // folders on a source typo (found via a real end-to-end run against production data, not a
        // hypothetical). "911 Carrera" is included to confirm the fix doesn't touch unrelated names.
        Assert.Equal(expectedModelName, PorscheCombinedPdfLayout.ExtractModelName(headerText));
    }

    // Page texts below are PdfPig's page.Text of the real 2025 combined PDF (trimmed), including its
    // glued words.
    [Theory]
    [InlineData("07/20241 of 4Porsche AG, 9112 door, 4 seaterCoupe, as from model year 2025VersionPageID no. WP0_Porsche_911__Coupé_2025_2d_GD_GB_V001Additional information", "WP0_PORSCHE_911__COUPÉ_2025_2D_GD_GB_V001")]
    [InlineData("Page 3 of 6ID no. ENUS-01-710-0040Version no. 1Marking of the hybrid components", "ENUS-01-710-0039")]
    [InlineData("Porsche AG, Panamera (G3)All derivatives, except E-HybridSedan, as from model year 202412/2023ID no. GB-?Additional information", "Panamera (G3)All derivatives, except E-HybridSedan, as from model year 2024")]
    [InlineData("Page 1ID no. ENUS-01-710-0040Version no. 1Airbag", "ENUS-01-710-0040")]
    [InlineData("Page 1AirbagGas generator", null)]
    public void GetPageKey_HandlesNewIdsMisprintedIdsAndPlaceholderIds(string pageText, string? expectedKey)
    {
        Assert.Equal(expectedKey, PorscheCombinedPdfLayout.GetPageKey(pageText));
    }

    [Theory]
    [InlineData("07/20241 of 4Porsche AG, 9112 door, 4 seaterCoupe, as from model year 2025VersionPage", "911", 2025)]
    [InlineData("1 of 4Porsche AG, 911Porsche AG, 9112 door, 4 seater2 door, 4 seaterCoupe THEV, as from model year 2025Coupe THEV", "911", 2025)]
    [InlineData("12/2023ID no. GB-?1 of 4Porsche AG, Panamera E-Hybrid (G3)All derivativesSedan, as from model year 2024Additional", "Panamera E-Hybrid", 2024)]
    public void Parse_NewLayoutHeaders_ExtractModelAndYear(string pageText, string expectedModel, int expectedFrom)
    {
        var parsed = PorscheCombinedPdfLayout.Parse("key", [pageText]);

        Assert.Equal(expectedModel, parsed.ModelName);
        Assert.Equal(expectedFrom, parsed.BuildYearFrom);
    }

    [Fact]
    public void Parse_KnownHeaderlessSheet_UsesItsCheckedModelName()
    {
        var parsed = PorscheCombinedPdfLayout.Parse("ENUS-01-710-0039", ["ID no. ENUS-01-710-0039Version no. 1Page 1 of 6Airbag"]);

        Assert.Equal("Cayenne E-Hybrid", parsed.ModelName);
    }

    [Theory]
    [InlineData(new[] { "Page 1 of 3", "Page 2 of 3", "Page 3 of 3" }, false)]
    [InlineData(new[] { "Page 1 of 3", "Page 3 of 3" }, true)] // a page went missing
    [InlineData(new[] { "Page 1 of 2", "Page 2 of 2", "Page 2 of 6" }, true)] // a foreign page joined
    [InlineData(new[] { "Page 1 of 68", "Page 2 of 65" }, true)] // glued text, read as "of 6": two pages missing
    [InlineData(new[] { "Page 1 of 28", "Page 2 of 24" }, false)]
    [InlineData(new[] { "Page 1" }, false)] // one-page sheets state no total
    [InlineData(new[] { "07/20241 of 4Porsche AG, 911", "2 of 4Additional" }, false)] // 2025 layout, unreadable
    public void PageNumberingMismatch_FlagsMissingOrForeignPages(string[] pageTexts, bool expected)
    {
        Assert.Equal(expected, PorscheCombinedPdfLayout.PageNumberingMismatch(pageTexts));
    }

    [Fact]
    public void Parse_KnownIdWithAHeader_UsesTheHeader()
    {
        var parsed = PorscheCombinedPdfLayout.Parse("ENUS-01-710-0077", ["ID no. ENUS-01-710-0077Version no. 1Page 1Porsche AG, Macan (95B) all derivatives, SUVfrom Model Year 2014"]);

        Assert.Equal("Macan", parsed.ModelName);
    }

    [Fact]
    public void Parse_UnknownHeaderlessSheet_FallsBackToItsId()
    {
        var parsed = PorscheCombinedPdfLayout.Parse("ENUS-01-710-0999", ["ID no. ENUS-01-710-0999Version no. 1Page 1Airbag"]);

        Assert.Equal("ENUS-01-710-0999", parsed.ModelName);
        Assert.Equal(Rettungskarten.Core.Models.ParseConfidence.Unparsed, parsed.ParseConfidence);
    }
}
