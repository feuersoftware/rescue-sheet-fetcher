using System.Text;

namespace Rettungskarten.Core.Reporting;

/// <summary>
/// Formats the priority report as CSV, alongside PrioritizeCommand's existing JSON output - some
/// consumers (procurement/administrative staff, not just developers) find a spreadsheet-friendly file
/// more useful than JSON. Pure logic, no I/O: the caller (PrioritizeCommand) writes the returned string
/// to disk (with a UTF-8 BOM - see that call site's comment for why). No CSV library needed for a flat,
/// non-nested table this size (at most a few thousand rows) - <see cref="PriorityReportRow.ModelName"/>
/// is the only field that's ever free text with commas/quotes/newlines.
///
/// Enum fields (Brand/BundlePriority) are rendered as their literal C# names (e.g. "High", not JSON's
/// camelCase "high", and not a localized/translated value) - deliberately, not by accident: this keeps
/// the CSV's content stable regardless of which --lang a given run used, unlike the console summary
/// output (which is deliberately localized via DisplayText, since that's read once, live, not
/// archived).
/// </summary>
public static class PriorityReportCsvFormatter
{
    public static string Format(IEnumerable<PriorityReportRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Brand,ModelName,CardCount,NotDownloadedCount,EstimatedFleetSize,BundlePriority");

        foreach (var row in rows)
        {
            var fields = new[]
            {
                row.Brand.ToString(), row.ModelName, row.CardCount.ToString(), row.NotDownloadedCount.ToString(),
                row.EstimatedFleetSize?.ToString(), row.BundlePriority.ToString()
            };
            builder.AppendLine(string.Join(',', fields.Select(Escape)));
        }

        return builder.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
