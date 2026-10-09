using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Splitting;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixture (fiat_collection_sample_pages.pdf, ~560KB) is built from the real 47-page, 6.3MB Fiat
/// "ShedaSoccorso" collection: its three table-of-contents pages (2-4, link annotations stripped - they
/// reference every page of the original and would otherwise drag the whole document into the copy),
/// then the real sheets 10 (Grande Punto 3, whose drawing carries a stray door badge "3"), 37 (Qubo,
/// "(AB_01/2008)") and 6 (Doblò Natural Power, "(BIS_12/2009)") - deliberately out of order and not at
/// the positions the table implies, so only the footer numbers can map them right.
/// </summary>
public class StellantisCollectionCombinedPdfLayoutTests
{
    private static byte[] LoadFixture() => File.ReadAllBytes(Path.Combine("Fixtures", "fiat_collection_sample_pages.pdf"));

    private static StellantisCollectionCombinedPdfLayout FiatLayout() => new(Brand.Fiat, "Fiat");

    [Fact]
    public void ReadContents_ReadsEveryLineOfTheRealTable()
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(LoadFixture());

        var contents = StellantisCollectionCombinedPdfLayout.ReadContents(document);

        Assert.Equal(Enumerable.Range(1, 43), contents.Keys.Order());
        Assert.Equal("FIAT 500", contents[1]);
        Assert.Equal("FIAT GRANDE PUNTO LPG 3", contents[12]);
        // Number and line on different baselines (8pt apart on page 3).
        Assert.Equal("FIAT PANDA LPG", contents[23]);
        // A date on a baseline of its own.
        Assert.Equal("FIAT DOBLÒ 01/2010", contents[7]);
        Assert.Equal("FIAT ULYSSE", contents[43]);
    }

    [Fact]
    public void Split_MapsEachSheetByItsFooterNumber_NotItsPosition()
    {
        var outcome = CombinedPdfSplitter.SplitDocument(LoadFixture(), FiatLayout());

        Assert.Equal(["10 FIAT GRANDE PUNTO 3", "37 FIAT QUBO 01/2008", "6 FIAT DOBLÒ NATURAL POWER 12/2009"],
            outcome.Parts.Select(p => p.DocumentId));
        Assert.Empty(outcome.UnassignedPageNumbers);
        Assert.All(outcome.Parts, p =>
        {
            using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(p.PdfBytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            Assert.Equal(1, pdf.PageCount);
        });
    }

    [Fact]
    public void Split_ParsesTheTableLine_AndTakesYearsFromTheSheet()
    {
        var parts = CombinedPdfSplitter.Split(LoadFixture(), FiatLayout());

        var grandePunto = parts[0].Parsed;
        Assert.Equal("Grande Punto", grandePunto.ModelName);
        Assert.Equal(3, grandePunto.Doors);
        Assert.Equal("FIAT GRANDE PUNTO 3", grandePunto.Variant);

        // The table prints "01/2008" for "ab 01/2008", the sheet "(AB_01/2008)".
        var qubo = parts[1].Parsed;
        Assert.Equal("Qubo", qubo.ModelName);
        Assert.Equal(2008, qubo.BuildYearFrom);
        Assert.Null(qubo.BuildYearTo);

        // ... and "12/2009" for "bis 12/2009" - read from the table alone, this would be a start year.
        var doblo = parts[2].Parsed;
        Assert.Equal("Doblò", doblo.ModelName);
        Assert.Equal("Erdgas", doblo.FuelType);
        Assert.Null(doblo.BuildYearFrom);
        Assert.Equal(2009, doblo.BuildYearTo);
        Assert.All(parts, p => Assert.Equal("DE", p.Parsed.LanguageCode));
    }

    [Theory]
    [InlineData("ABARTH 695 TRIBUTO FERRARI", "Abarth", "695", null, null)]
    [InlineData("ABARTH PUNTO EVO", "Abarth", "Punto EVO", null, null)]
    [InlineData("FIAT PUNTO EVO NATURAL POWER 5", "Fiat", "Punto EVO", "Erdgas", 5)]
    [InlineData("FIAT STILO MULTIWAGON", "Fiat", "Stilo Multiwagon", null, null)]
    [InlineData("FIAT 500L", "Fiat", "500L", null, null)]
    public void ParseEntry_TableLines(string entry, string brandPrefix, string model, string? fuel, int? doors)
    {
        var parsed = StellantisCollectionCombinedPdfLayout.ParseEntry(entry, "Fiat Group Automobiles SpA 09/2010 1", brandPrefix);

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(fuel, parsed.FuelType);
        Assert.Equal(doors, parsed.Doors);
        Assert.Equal(entry, parsed.Variant);
        Assert.Null(parsed.BuildYearFrom); // the footer's "09/2010" is a revision date
    }

    [Fact]
    public void ParseEntry_KeepsAYearTheLineStates_WhenTheSheetHasNone()
    {
        var parsed = StellantisCollectionCombinedPdfLayout.ParseEntry("FIAT PANDA 2012 5", "Fiat Group Automobiles SpA 01/2012 21", "Fiat");

        Assert.Equal("Panda", parsed.ModelName);
        Assert.Equal(2012, parsed.BuildYearFrom);
        Assert.Equal(5, parsed.Doors);
    }

    [Fact]
    public void Layout_IsRegisteredForFiatAndAbarth_NotFiatProfessional()
    {
        Assert.Single(CombinedPdfLayouts.For(Brand.Fiat));
        Assert.Single(CombinedPdfLayouts.For(Brand.Abarth));
        Assert.Empty(CombinedPdfLayouts.For(Brand.FiatProfessional));
    }
}
