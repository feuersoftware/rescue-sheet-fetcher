using ClosedXML.Excel;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.Stock;

/// <summary>
/// Parses the KBA FZ12 "Bestand an Personenkraftwagen nach Segmenten und Modellreihen" workbook.
/// Schema verified against a real fz12_2026.xlsx download (worksheet "FZ 12.1"):
/// header row has "Segment" in column B and "Modellreihe" in column C, data starts two rows below;
/// column B (Segment) is blank on every row but the first of a segment (carry-forward), column C
/// combines brand and model in one string ("VW GOLF", "SKODA OCTAVIA" - no separate brand column),
/// column D is the current-year "Anzahl" as a plain number. The table ends at a "BESTAND INSGESAMT"
/// row; segment/grand-total summary rows in between (blank Modellreihe) are skipped, not parsed as
/// model rows.
/// </summary>
public static class KbaStockXlsxParser
{
    private const string SheetName = "FZ 12.1";
    private const string GrandTotalMarker = "BESTAND INSGESAMT";
    private const string License = "Datenlizenz Deutschland - Namensnennung - Version 2.0 (dl-de/by-2-0)";

    public static VehicleStockResult Parse(byte[] xlsxContent, int year, string sourceUrl)
    {
        using var stream = new MemoryStream(xlsxContent);
        using var workbook = new XLWorkbook(stream);

        var worksheet = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals(SheetName, StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.FirstOrDefault(w => w.Name.StartsWith("FZ 12", StringComparison.OrdinalIgnoreCase));

        if (worksheet is null)
        {
            throw new InvalidOperationException(Strings.Get("Stock_WorksheetNotFound", SheetName, year));
        }

        var range = worksheet.RangeUsed();
        if (range is null)
        {
            throw new InvalidOperationException(Strings.Get("Stock_WorksheetEmpty", worksheet.Name, year));
        }

        var rowCount = range.RowCount();
        var headerRow = FindHeaderRow(range, rowCount);
        if (headerRow < 0)
        {
            throw new InvalidOperationException(Strings.Get("Stock_HeaderRowNotFound", year));
        }

        var warnings = new List<string>();
        var headerText = range.Cell(headerRow, 3).GetString();
        if (!headerText.Contains(year.ToString(), StringComparison.Ordinal))
        {
            warnings.Add(Strings.Get("Stock_HeaderYearMismatch", headerText, year));
        }

        var rows = ParseDataRows(range, headerRow + 2, rowCount, warnings);
        var referenceDate = new DateOnly(year, 1, 1);
        return new VehicleStockResult(year, referenceDate, rows, warnings, sourceUrl, License);
    }

    private static int FindHeaderRow(IXLRange range, int rowCount)
    {
        for (var r = 1; r <= Math.Min(rowCount, 20); r++)
        {
            if (range.Cell(r, 1).GetString().Trim().Equals("Segment", StringComparison.OrdinalIgnoreCase) &&
                range.Cell(r, 2).GetString().Trim().Equals("Modellreihe", StringComparison.OrdinalIgnoreCase))
            {
                return r;
            }
        }

        return -1;
    }

    private static List<VehicleStockRow> ParseDataRows(IXLRange range, int firstDataRow, int rowCount, List<string> warnings)
    {
        var rows = new List<VehicleStockRow>();
        var currentSegment = string.Empty;

        for (var r = firstDataRow; r <= rowCount; r++)
        {
            var segmentCell = range.Cell(r, 1).GetString().Trim();
            var modelCell = range.Cell(r, 2).GetString().Trim();

            if (segmentCell.Length > 0)
            {
                currentSegment = segmentCell;
            }

            if (modelCell.Length == 0)
            {
                if (segmentCell.Equals(GrandTotalMarker, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                continue;
            }

            var countCell = range.Cell(r, 3);
            if (countCell.DataType != XLDataType.Number)
            {
                warnings.Add(Strings.Get("Stock_RowNotANumber", r, modelCell, countCell.GetString()));
                continue;
            }

            var count = (int)countCell.GetValue<double>();
            var (brandLabel, modelSeries) = SplitBrandAndModel(modelCell);
            rows.Add(new VehicleStockRow(currentSegment, brandLabel, modelSeries, count));
        }

        return rows;
    }

    private static (string BrandLabel, string ModelSeries) SplitBrandAndModel(string modellreihe)
    {
        var spaceIndex = modellreihe.IndexOf(' ');
        return spaceIndex < 0
            ? (modellreihe, string.Empty)
            : (modellreihe[..spaceIndex], modellreihe[(spaceIndex + 1)..].Trim());
    }
}
