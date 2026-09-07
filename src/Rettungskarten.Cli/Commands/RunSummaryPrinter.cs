using Rettungskarten.Core.Models;

namespace Rettungskarten.Cli.Commands;

public static class RunSummaryPrinter
{
    public static void Print(IReadOnlyList<BrandRunResult> results)
    {
        Console.WriteLine();
        Console.WriteLine($"{"Marke",-10} {"Status",-16} {"Entdeckt",8} {"Geladen",8} {"Nur Metadaten",14} {"Hinweis",-40}");
        Console.WriteLine(new string('-', 100));

        foreach (var r in results)
        {
            var note = r.Outcome switch
            {
                BrandRunOutcome.NotImplemented => r.Note ?? "nicht implementiert",
                BrandRunOutcome.DiscoveryFailed => r.Note ?? "Discovery fehlgeschlagen",
                _ => string.Empty
            };

            Console.WriteLine(
                $"{r.Brand,-10} {r.Outcome,-16} {r.Discovered,8} {r.Downloaded,8} {r.MetadataOnly,14} {Truncate(note, 40),-40}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// True if any brand failed unexpectedly (discovery broke, likely a site layout change) - as
    /// opposed to expected steady-state conditions like VW's known 403 or a brand stub, which should
    /// not make the process exit non-zero.
    /// </summary>
    public static bool HasUnexpectedFailures(IReadOnlyList<BrandRunResult> results) =>
        results.Any(r => r.Outcome == BrandRunOutcome.DiscoveryFailed);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
