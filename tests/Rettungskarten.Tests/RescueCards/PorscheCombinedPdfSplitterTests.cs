using Rettungskarten.Infrastructure.RescueCards.Splitting;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Regression test against a real 16-page slice of Porsche's combined PDF (extracted with pikepdf so
/// unreferenced shared resources are dropped - a plain per-page split of the original balloons to the
/// size of the whole 55MB document, since it doesn't garbage-collect shared objects). The slice covers
/// six real models: four single-page ones and two that span multiple physical pages (5 and 6 pages),
/// so both grouping shapes are exercised against real content, not a synthetic approximation.
/// </summary>
public class PorscheCombinedPdfSplitterTests
{
    private static byte[] LoadFixture() => File.ReadAllBytes(Path.Combine("Fixtures", "porsche_sample_pages.pdf"));

    [Fact]
    public void Split_GroupsPagesByModelId_SixModelsFromSixteenPages()
    {
        var results = PorscheCombinedPdfSplitter.Split(LoadFixture());

        Assert.Equal(6, results.Count);
    }

    [Fact]
    public void Split_SinglePageModel_ExtractsNameAndYearRange()
    {
        var results = PorscheCombinedPdfSplitter.Split(LoadFixture());

        var cayenne2003 = Assert.Single(results, r => r.Parsed.BuildYearFrom == 2003);
        Assert.Equal("Cayenne", cayenne2003.Parsed.ModelName);
        Assert.Equal(2005, cayenne2003.Parsed.BuildYearTo);
        Assert.Equal("SUV", cayenne2003.Parsed.BodyType);
        Assert.Equal("EN", cayenne2003.Parsed.LanguageCode);
    }

    [Fact]
    public void Split_OpenEndedYearRange_ParsesFromModelYearOnly()
    {
        var results = PorscheCombinedPdfSplitter.Split(LoadFixture());

        var hybrid = Assert.Single(results, r => r.Parsed.Variant != null && r.Parsed.Variant.StartsWith("Cayenne S Hybrid (92A)"));
        Assert.Equal(2011, hybrid.Parsed.BuildYearFrom);
        Assert.Null(hybrid.Parsed.BuildYearTo);
    }

    [Fact]
    public void Split_MultiPageModel_CombinesAllPagesIntoOnePdf()
    {
        var results = PorscheCombinedPdfSplitter.Split(LoadFixture());

        // "Cayenne S Hybrid (92A)" spans 5 physical pages (Page 1 of 5 .. Page 5 of 5) - all of them
        // must end up as one 5-page output PDF, not five separate single-page ones.
        var fivePageModel = Assert.Single(results, r => r.Parsed.Variant != null && r.Parsed.Variant.StartsWith("Cayenne S Hybrid"));

        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(fivePageModel.PdfBytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(5, pdf.PageCount);
    }

    [Fact]
    public void Split_EveryResult_ProducesValidPdfBytes()
    {
        var results = PorscheCombinedPdfSplitter.Split(LoadFixture());

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
        var results = PorscheCombinedPdfSplitter.Split(LoadFixture());

        var distinctIds = results.Select(r => r.DocumentId).Distinct().ToList();
        Assert.Equal(results.Count, distinctIds.Count);
        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.DocumentId)));
    }

    [Fact]
    public void Split_LeadingLegalNoticePage_ProducesNoSpuriousEntry()
    {
        // The fixture's first page is a legal-notice page with no "ID no." footer at all - it must be
        // silently skipped, not turned into a crash or a garbage entry with no model info.
        var results = PorscheCombinedPdfSplitter.Split(LoadFixture());

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
        Assert.Equal(expectedModelName, PorscheCombinedPdfSplitter.ExtractModelName(headerText));
    }
}
