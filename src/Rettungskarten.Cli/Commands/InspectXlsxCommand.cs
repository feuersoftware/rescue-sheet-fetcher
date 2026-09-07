using System.CommandLine;
using ClosedXML.Excel;

namespace Rettungskarten.Cli.Commands;

/// <summary>
/// Developer utility for verifying a KBA (or similar) workbook's schema before adjusting a parser -
/// this is how the FZ12 layout was originally verified against a real download. Kept as a permanent
/// CLI tool rather than a throwaway script, since KBA's file layout can change between years.
/// </summary>
public static class InspectXlsxCommand
{
    public static Command Build()
    {
        var pathArgument = new Argument<string>("path") { Description = "Pfad zur XLSX-Datei" };
        var rowsOption = new Option<int>("--rows") { Description = "Maximale Zeilenanzahl je Arbeitsblatt", DefaultValueFactory = _ => 15 };

        var command = new Command("xlsx", "Zeigt Arbeitsblätter/Zeilen einer XLSX-Datei zur Schema-Prüfung an");
        command.Add(pathArgument);
        command.Add(rowsOption);

        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathArgument)!;
            var maxRows = parseResult.GetValue(rowsOption);

            using var workbook = new XLWorkbook(path);
            foreach (var worksheet in workbook.Worksheets)
            {
                var range = worksheet.RangeUsed();
                Console.WriteLine($"=== '{worksheet.Name}' ({range?.RangeAddress}) ===");
                if (range is null)
                {
                    continue;
                }

                var rows = Math.Min(range.RowCount(), maxRows);
                var cols = Math.Min(range.ColumnCount(), 8);
                for (var r = 1; r <= rows; r++)
                {
                    var cells = Enumerable.Range(1, cols).Select(c => range.Cell(r, c).GetString());
                    Console.WriteLine(string.Join(" | ", cells));
                }

                Console.WriteLine();
            }

            return 0;
        });

        return command;
    }
}
