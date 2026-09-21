using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Cli.Commands;

public static class RunSummaryPrinter
{
    private const int NoteWidth = 40;

    public static void Print(IReadOnlyList<BrandRunResult> results)
    {
        var brandCol = Strings.Get("Summary_Column_Brand");
        var statusCol = Strings.Get("Summary_Column_Status");
        var discoveredCol = Strings.Get("Summary_Column_Discovered");
        var downloadedCol = Strings.Get("Summary_Column_Downloaded");
        var metadataOnlyCol = Strings.Get("Summary_Column_MetadataOnly");
        var failedCol = Strings.Get("Summary_Column_Failed");
        var noteCol = Strings.Get("Summary_Column_Note");

        // Every column's width must fit whichever language is actually rendered - German text
        // ("Discovery fehlgeschlagen", "Fehlgeschlagen") runs noticeably longer than the English
        // source, so a width sized for one language silently misaligns every column that follows it
        // under the other (verified: "Fehlgeschlagen" alone overflows a fixed width of 8). Computed
        // from the actual header/value lengths for every column except the free-text Note column,
        // which is deliberately capped/truncated rather than grown to fit arbitrarily long text.
        var statusWidth = ColumnWidth(statusCol, results, r => DisplayText.For(r.Outcome));
        var brandWidth = ColumnWidth(brandCol, results, r => r.Brand.ToString());
        var discoveredWidth = ColumnWidth(discoveredCol, results, r => r.Discovered);
        var downloadedWidth = ColumnWidth(downloadedCol, results, r => r.Downloaded);
        var metadataOnlyWidth = ColumnWidth(metadataOnlyCol, results, r => r.MetadataOnly);
        var failedWidth = ColumnWidth(failedCol, results, r => r.Failed);

        Console.WriteLine();
        Console.WriteLine($"{brandCol.PadRight(brandWidth)} {statusCol.PadRight(statusWidth)} {discoveredCol.PadLeft(discoveredWidth)} {downloadedCol.PadLeft(downloadedWidth)} {metadataOnlyCol.PadLeft(metadataOnlyWidth)} {failedCol.PadLeft(failedWidth)} {noteCol,-NoteWidth}");
        Console.WriteLine(new string('-', brandWidth + 1 + statusWidth + 1 + discoveredWidth + 1 + downloadedWidth + 1 + metadataOnlyWidth + 1 + failedWidth + 1 + NoteWidth));

        foreach (var r in results)
        {
            var note = r.Outcome switch
            {
                BrandRunOutcome.NotImplemented => r.Note ?? Strings.Get("Summary_Note_NotImplemented"),
                BrandRunOutcome.DiscoveryFailed => r.Note ?? Strings.Get("Summary_Note_DiscoveryFailed"),
                _ => string.Empty
            };

            Console.WriteLine(
                $"{r.Brand.ToString().PadRight(brandWidth)} {DisplayText.For(r.Outcome).PadRight(statusWidth)} {r.Discovered.ToString().PadLeft(discoveredWidth)} {r.Downloaded.ToString().PadLeft(downloadedWidth)} {r.MetadataOnly.ToString().PadLeft(metadataOnlyWidth)} {r.Failed.ToString().PadLeft(failedWidth)} {Truncate(note, NoteWidth),-NoteWidth}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// True if any brand failed unexpectedly (discovery broke, likely a site layout change) - as
    /// opposed to expected steady-state conditions like Cupra AT's known auth-gateway redirect or a
    /// brand stub, which should not make the process exit non-zero.
    ///
    /// A completed run that discovered zero entries counts as unexpected too: a scraper selector that
    /// breaks without throwing (a manufacturer restructures its page HTML so nothing matches) makes
    /// DiscoverAsync return an empty list instead of an exception, which previously looked identical to
    /// a healthy run here - exactly the "a brand quietly stops discovering any rescue cards" regression
    /// the weekly link-check workflow exists to catch (see link-check.yml).
    /// </summary>
    public static bool HasUnexpectedFailures(IReadOnlyList<BrandRunResult> results) =>
        results.Any(r => r.Outcome == BrandRunOutcome.DiscoveryFailed
            || (r.Outcome == BrandRunOutcome.Completed && r.Discovered == 0));

    private static int ColumnWidth(string header, IReadOnlyList<BrandRunResult> results, Func<BrandRunResult, string> selector) =>
        Math.Max(header.Length, results.Count == 0 ? 0 : results.Max(r => selector(r).Length));

    private static int ColumnWidth(string header, IReadOnlyList<BrandRunResult> results, Func<BrandRunResult, int> selector) =>
        ColumnWidth(header, results, r => selector(r).ToString());

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
