using System.CommandLine;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Cli.Commands;

public static class ListBrandsCommand
{
    private static (Brand Brand, string Status, string Source)[] BuildInfo() =>
    [
        (Brand.VW, Strings.Get("Brand_VW_Status"), Strings.Get("Brand_VW_Source")),
        (Brand.Audi, Strings.Get("Status_FullyFunctional"), Strings.Get("Brand_Audi_Source")),
        (Brand.Skoda, Strings.Get("Status_FullyFunctional"), Strings.Get("Brand_Skoda_Source")),
        (Brand.Seat, Strings.Get("Status_FullyFunctional"), Strings.Get("Brand_Seat_Source")),
        (Brand.Cupra, Strings.Get("Brand_Cupra_Status"), Strings.Get("Brand_Cupra_Source")),
        (Brand.Porsche, Strings.Get("Brand_Porsche_Status"), Strings.Get("Brand_Porsche_Source"))
    ];

    public static Command Build()
    {
        var command = new Command("brands", Strings.Get("Command_ListBrands_Description"));
        command.SetAction(_ =>
        {
            var brandCol = Strings.Get("Summary_Column_Brand");
            var statusCol = Strings.Get("Summary_Column_Status");
            var sourceCol = Strings.Get("ListBrands_Column_Source");

            Console.WriteLine($"{brandCol,-10} {statusCol,-45} {sourceCol,-40}");
            Console.WriteLine(new string('-', 95));
            foreach (var (brand, status, source) in BuildInfo())
            {
                Console.WriteLine($"{brand,-10} {status,-45} {source,-40}");
            }

            return 0;
        });

        return command;
    }
}
