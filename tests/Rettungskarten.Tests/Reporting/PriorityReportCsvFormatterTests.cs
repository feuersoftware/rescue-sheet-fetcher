using Rettungskarten.Core.Models;
using Rettungskarten.Core.Reporting;

namespace Rettungskarten.Tests.Reporting;

public class PriorityReportCsvFormatterTests
{
    private static PriorityReportRow Row(
        string modelName, int cardCount, int? fleetSize, BundlePriority priority, int notDownloadedCount = 0) =>
        new(Brand.VW, modelName, cardCount, notDownloadedCount, fleetSize, priority);

    [Fact]
    public void Format_IncludesHeaderAndOneRowPerModel()
    {
        var csv = PriorityReportCsvFormatter.Format([Row("Golf", 8, 3_231_990, BundlePriority.High)]);

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Brand,ModelName,CardCount,NotDownloadedCount,EstimatedFleetSize,BundlePriority", lines[0]);
        Assert.Equal("VW,Golf,8,0,3231990,High", lines[1]);
    }

    [Fact]
    public void Format_ModelNameWithComma_IsQuoted()
    {
        var csv = PriorityReportCsvFormatter.Format([Row("Kodiaq, Kodiaq RS", 2, 174_768, BundlePriority.High)]);

        Assert.Contains("\"Kodiaq, Kodiaq RS\"", csv);
    }

    [Fact]
    public void Format_ModelNameWithQuote_IsEscapedByDoubling()
    {
        var csv = PriorityReportCsvFormatter.Format([Row("Say \"hello\"", 1, null, BundlePriority.Unknown)]);

        Assert.Contains("\"Say \"\"hello\"\"\"", csv);
    }

    [Fact]
    public void Format_NullModelNameAndFleetSize_RenderAsEmptyField()
    {
        var csv = PriorityReportCsvFormatter.Format([new PriorityReportRow(Brand.VW, null, 1, 0, null, BundlePriority.Unknown)]);

        var dataLine = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[1];
        Assert.Equal("VW,,1,0,,Unknown", dataLine);
    }

    [Fact]
    public void Format_NotDownloadedCount_RendersAsOwnColumn()
    {
        var csv = PriorityReportCsvFormatter.Format([Row("Golf", 8, 3_231_990, BundlePriority.High, notDownloadedCount: 2)]);

        var dataLine = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[1];
        Assert.Equal("VW,Golf,8,2,3231990,High", dataLine);
    }

    [Fact]
    public void Format_ResultParsesBackToTheSameNumberOfColumnsPerRow()
    {
        var rows = new[]
        {
            Row("Golf, GTI", 8, 3_231_990, BundlePriority.High),
            Row("Up", 2, 15_000, BundlePriority.Low),
        };

        var csv = PriorityReportCsvFormatter.Format(rows);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length); // header + 2 rows
    }
}
