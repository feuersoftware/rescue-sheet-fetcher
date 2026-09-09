using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Cli.Commands;

public static class RunSummaryPrinter
{
    public static void Print(IReadOnlyList<BrandRunResult> results)
    {
        var brandCol = Strings.Get("Summary_Column_Brand");
        var statusCol = Strings.Get("Summary_Column_Status");
        var discoveredCol = Strings.Get("Summary_Column_Discovered");
        var downloadedCol = Strings.Get("Summary_Column_Downloaded");
        var metadataOnlyCol = Strings.Get("Summary_Column_MetadataOnly");
        var noteCol = Strings.Get("Summary_Column_Note");

        // The Status column's fixed width must fit whichever language is actually rendered - German
        // outcome text ("Discovery fehlgeschlagen") runs noticeably longer than the English source
        // ("Discovery failed"), so a width sized for one language silently misaligns every column
        // that follows it under the other. Compute it from the actual header/values instead. The
        // Brand column has the same problem since Lamborghini's name is longer than the fixed 10
        // chars every other brand fit in.
        var statuses = results.Select(r => DisplayText.For(r.Outcome)).ToList();
        var statusWidth = Math.Max(statusCol.Length, statuses.Count == 0 ? 0 : statuses.Max(s => s.Length));
        var brandWidth = Math.Max(brandCol.Length, results.Count == 0 ? 0 : results.Max(r => r.Brand.ToString().Length));

        Console.WriteLine();
        Console.WriteLine($"{brandCol.PadRight(brandWidth)} {statusCol.PadRight(statusWidth)} {discoveredCol,8} {downloadedCol,8} {metadataOnlyCol,14} {noteCol,-40}");
        Console.WriteLine(new string('-', brandWidth + 1 + statusWidth + 1 + 8 + 1 + 8 + 1 + 14 + 1 + 40));

        foreach (var r in results)
        {
            var note = r.Outcome switch
            {
                BrandRunOutcome.NotImplemented => r.Note ?? Strings.Get("Summary_Note_NotImplemented"),
                BrandRunOutcome.DiscoveryFailed => r.Note ?? Strings.Get("Summary_Note_DiscoveryFailed"),
                _ => string.Empty
            };

            Console.WriteLine(
                $"{r.Brand.ToString().PadRight(brandWidth)} {DisplayText.For(r.Outcome).PadRight(statusWidth)} {r.Discovered,8} {r.Downloaded,8} {r.MetadataOnly,14} {Truncate(note, 40),-40}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// True if any brand failed unexpectedly (discovery broke, likely a site layout change) - as
    /// opposed to expected steady-state conditions like Cupra AT's known auth-gateway redirect or a
    /// brand stub, which should not make the process exit non-zero.
    /// </summary>
    public static bool HasUnexpectedFailures(IReadOnlyList<BrandRunResult> results) =>
        results.Any(r => r.Outcome == BrandRunOutcome.DiscoveryFailed);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
