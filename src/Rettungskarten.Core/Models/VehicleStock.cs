namespace Rettungskarten.Core.Models;

public sealed record VehicleStockRow(string Segment, string BrandLabel, string ModelSeries, int Count);

public sealed record VehicleStockResult(
    int Year,
    DateOnly ReferenceDate,
    IReadOnlyList<VehicleStockRow> Rows,
    IReadOnlyList<string> UnparsedRowWarnings,
    string SourceUrl,
    string License);
