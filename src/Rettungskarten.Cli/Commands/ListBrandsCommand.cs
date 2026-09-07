using System.CommandLine;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Cli.Commands;

public static class ListBrandsCommand
{
    private static readonly (Brand Brand, string Status, string Source)[] Info =
    [
        (Brand.VW, "Discovery ok, Download blockiert (HTTP 403)", "assets.feature-app.io JSON-Feed"),
        (Brand.Audi, "Voll funktionsfähig", "audi.com statische HTML-Seite"),
        (Brand.Skoda, "Voll funktionsfähig", "skoda-auto.de Modellseiten"),
        (Brand.Seat, "Voll funktionsfähig", "seat.de Modellseiten"),
        (Brand.Cupra, "Voll funktionsfähig (nur Schweiz-Quelle)", "cupraofficial.ch statische HTML-Seite"),
        (Brand.Porsche, "Nicht implementiert", "keine bestätigte öffentliche Quelle gefunden")
    ];

    public static Command Build()
    {
        var command = new Command("brands", "Listet unterstützte Marken und deren Implementierungsstatus");
        command.SetAction(_ =>
        {
            Console.WriteLine($"{"Marke",-10} {"Status",-45} {"Quelle",-40}");
            Console.WriteLine(new string('-', 95));
            foreach (var (brand, status, source) in Info)
            {
                Console.WriteLine($"{brand,-10} {status,-45} {source,-40}");
            }

            return 0;
        });

        return command;
    }
}
