using System.CommandLine;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Cli.Commands;

public static class ListBrandsCommand
{
    private static (Brand Brand, string Status, string Source)[] BuildInfo() =>
    [
        (Brand.VW, Strings.Get("Status_FullyFunctional"), Strings.Get("Brand_VW_Source")),
        (Brand.Audi, Strings.Get("Status_FullyFunctional"), Strings.Get("Brand_Audi_Source")),
        (Brand.Skoda, Strings.Get("Status_FullyFunctional"), Strings.Get("Brand_Skoda_Source")),
        (Brand.Seat, Strings.Get("Status_FullyFunctional"), Strings.Get("Brand_Seat_Source")),
        (Brand.Cupra, Strings.Get("Brand_Cupra_Status"), Strings.Get("Brand_Cupra_Source")),
        (Brand.Porsche, Strings.Get("Brand_Porsche_Status"), Strings.Get("Brand_Porsche_Source")),
        // Porsche: functional but coarser-grained than every other brand - one combined PDF for all
        // current models plus a second for classic models, not a file per model (see
        // PorscheRescueCardSource); status text reflects that distinction rather than reusing
        // Status_FullyFunctional, which would imply the same per-model granularity as the rest.
        (Brand.Bentley, Strings.Get("Status_FullyFunctional"), Strings.Get("Brand_Bentley_Source")),
        (Brand.Lamborghini, Strings.Get("Brand_Lamborghini_Status"), Strings.Get("Brand_Lamborghini_Source"))
        // Lamborghini: same "English only, no German file" situation as Porsche - status text calls
        // that out instead of reusing Status_FullyFunctional.
    ];

    public static Command Build()
    {
        var command = new Command("brands", Strings.Get("Command_ListBrands_Description"));
        command.SetAction(_ =>
        {
            var brandCol = Strings.Get("Summary_Column_Brand");
            var statusCol = Strings.Get("Summary_Column_Status");
            var sourceCol = Strings.Get("ListBrands_Column_Source");

            var info = BuildInfo();
            // Fixed widths broke as soon as one language's status text (or, since Lamborghini, a
            // brand name) got longer than the others (see RunSummaryPrinter's identical fix) -
            // compute from actual content instead.
            var brandWidth = Math.Max(brandCol.Length, info.Max(i => i.Brand.ToString().Length));
            var statusWidth = Math.Max(statusCol.Length, info.Max(i => i.Status.Length));

            Console.WriteLine($"{brandCol.PadRight(brandWidth)} {statusCol.PadRight(statusWidth)} {sourceCol,-40}");
            Console.WriteLine(new string('-', brandWidth + 1 + statusWidth + 1 + 40));
            foreach (var (brand, status, source) in info)
            {
                Console.WriteLine($"{brand.ToString().PadRight(brandWidth)} {status.PadRight(statusWidth)} {source,-40}");
            }

            return 0;
        });

        return command;
    }
}
