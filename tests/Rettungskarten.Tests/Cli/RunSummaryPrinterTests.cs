using System.Globalization;
using Rettungskarten.Cli.Commands;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Tests.Cli;

/// <summary>
/// Strings.OverrideCulture is process-global static state, so every test that sets it resets it in
/// Dispose - otherwise a leaked override could make unrelated tests running in parallel flaky (see
/// StringsTests for the same pattern).
/// </summary>
public class RunSummaryPrinterTests : IDisposable
{
    public void Dispose() => Strings.OverrideCulture = null;

    [Fact]
    public void HasUnexpectedFailures_DiscoveryFailed_ReturnsTrue()
    {
        var results = new[] { BrandRunResult.DiscoveryFailed(Brand.VW, new InvalidOperationException("boom")) };

        Assert.True(RunSummaryPrinter.HasUnexpectedFailures(results));
    }

    [Fact]
    public void HasUnexpectedFailures_CompletedWithZeroDiscovered_ReturnsTrue()
    {
        // Regression test: a scraper selector that breaks without throwing (a manufacturer restructures
        // its page HTML so nothing matches) makes DiscoverAsync return an empty list, which previously
        // looked identical to a healthy run to this check - exactly the class of regression the weekly
        // link-check workflow exists to catch.
        var results = new[] { BrandRunResult.Completed(Brand.VW, []) };

        Assert.True(RunSummaryPrinter.HasUnexpectedFailures(results));
    }

    [Fact]
    public void HasUnexpectedFailures_CompletedWithEntries_ReturnsFalse()
    {
        var results = new[] { BrandRunResult.Completed(Brand.VW, [new ModelRunResult(Entry(), RescueCardStatus.Downloaded, null)]) };

        Assert.False(RunSummaryPrinter.HasUnexpectedFailures(results));
    }

    [Fact]
    public void HasUnexpectedFailures_NotImplementedStub_ReturnsFalse()
    {
        // A not-yet-implemented brand source discovers zero entries by design (no source is registered
        // to even attempt discovery) - that's a different, already-distinguished outcome from a
        // Completed run finding nothing, and must not trip this.
        var results = new[] { BrandRunResult.NotImplemented(Brand.VW, "no source registered") };

        Assert.False(RunSummaryPrinter.HasUnexpectedFailures(results));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void Print_HeaderSeparatorAndDataRows_AllHaveEqualLength(string lang)
    {
        // Regression test: a column header longer than the count columns' old fixed width of 8 (e.g.
        // German "Fehlgeschlagen", 14 chars) used to silently misalign the header against the
        // separator/data rows, since only the Brand/Status columns computed their width dynamically.
        // 217 downloads (a real VW-brand-size number) also exercises a count column growing past its
        // own header's width.
        Strings.OverrideCulture = new CultureInfo(lang);
        var results = new[]
        {
            BrandRunResult.Completed(Brand.VW, Enumerable.Range(0, 217)
                .Select(_ => new ModelRunResult(Entry(), RescueCardStatus.Downloaded, null)).ToList()),
        };

        var originalOut = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            RunSummaryPrinter.Print(results);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var lengths = lines.Select(l => l.Length).Distinct().ToList();
        Assert.Single(lengths);
    }

    private static RescueCardEntry Entry()
    {
        var parsed = new ParsedModelInfo(
            ModelName: "Golf", Variant: null, BodyType: null, BuildYearFrom: null, BuildYearTo: null,
            Doors: null, FuelType: null, LanguageCode: "DE", ParseConfidence: ParseConfidence.High);
        return new RescueCardEntry(Brand.VW, "https://example.test", "https://example.test/a.pdf", "a.pdf", parsed);
    }
}
