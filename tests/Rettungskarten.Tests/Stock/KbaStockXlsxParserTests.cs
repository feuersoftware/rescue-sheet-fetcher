using Rettungskarten.Infrastructure.Stock;

namespace Rettungskarten.Tests.Stock;

/// <summary>
/// Regression tests against a real fz12_2026.xlsx download, so a future KBA layout change is caught
/// immediately instead of silently producing empty/wrong results.
/// </summary>
public class KbaStockXlsxParserTests
{
    private static byte[] LoadFixture() => File.ReadAllBytes(Path.Combine("Fixtures", "fz12_2026.xlsx"));

    [Fact]
    public void Parse_ExtractsExpectedRowCountAndNoWarnings()
    {
        var result = KbaStockXlsxParser.Parse(LoadFixture(), 2026, "https://example.test/fz12_2026.xlsx");

        Assert.Equal(2026, result.Year);
        Assert.Equal(new DateOnly(2026, 1, 1), result.ReferenceDate);
        Assert.True(result.Rows.Count > 600, $"Expected >600 model rows, got {result.Rows.Count}");

        // A handful of rows legitimately carry a non-numeric marker instead of a count (see the
        // "Zeichenerklärung" legend on the file's table-of-contents sheet, e.g. "." for "Zahlenwert
        // unbekannt oder geheim zu halten") - those are expected to be skipped-with-warning, not
        // absent. The warning text itself is localized (varies with Strings.OverrideCulture), so
        // assert on the count and on the rows actually being skipped (Parse_SkipsNonNumericRow below)
        // rather than on any particular language's wording.
        Assert.Equal(3, result.UnparsedRowWarnings.Count);
    }

    [Theory]
    [InlineData("SSANGYONG", "KORANDO")]
    [InlineData("SSANGYONG", "TIVOLI")]
    [InlineData("SSANGYONG", "REXTON")]
    public void Parse_SkipsNonNumericRow(string brand, string modelSeries)
    {
        // These three rows carry a non-numeric count marker in the real fixture (see the comment
        // above) - verifies the actual invariant (the row is excluded from Rows) directly, instead of
        // via the localized warning text.
        var result = KbaStockXlsxParser.Parse(LoadFixture(), 2026, "https://example.test/fz12_2026.xlsx");

        Assert.DoesNotContain(result.Rows, r => r.BrandLabel == brand && r.ModelSeries == modelSeries);
    }

    [Theory]
    [InlineData("VW", "GOLF", 3231990)]
    [InlineData("VW", "TIGUAN", 811016)]
    [InlineData("AUDI", "A3", 708850)]
    [InlineData("SKODA", "OCTAVIA", 724473)]
    [InlineData("SEAT", "IBIZA", 408155)]
    [InlineData("PORSCHE", "TAYCAN", 13285)]
    public void Parse_FindsKnownModelRow(string brand, string modelSeries, int? expectedCount)
    {
        var result = KbaStockXlsxParser.Parse(LoadFixture(), 2026, "https://example.test/fz12_2026.xlsx");

        var row = result.Rows.SingleOrDefault(r => r.BrandLabel == brand && r.ModelSeries == modelSeries);

        if (expectedCount is null)
        {
            Assert.NotNull(row);
        }
        else
        {
            Assert.NotNull(row);
            Assert.Equal(expectedCount.Value, row!.Count);
        }
    }

    [Fact]
    public void Parse_DoesNotIncludeGrandTotalRow()
    {
        // The grand-total row ("BESTAND INSGESAMT", ~49 million vehicles) and the per-segment
        // subtotal rows above it (blank Modellreihe) must be excluded - only per-model-series rows
        // belong in the result. A per-segment "SONSTIGE" catch-all *model* row (blank ModelSeries
        // because the cell is the single word "SONSTIGE" with no space to split on) is legitimate
        // and expected to remain.
        var result = KbaStockXlsxParser.Parse(LoadFixture(), 2026, "https://example.test/fz12_2026.xlsx");

        Assert.DoesNotContain(result.Rows, r => r.Count > 10_000_000);
        Assert.DoesNotContain(result.Rows, r =>
            r.Segment.Equals("BESTAND INSGESAMT", StringComparison.OrdinalIgnoreCase) ||
            r.BrandLabel.Equals("BESTAND INSGESAMT", StringComparison.OrdinalIgnoreCase));
    }
}
