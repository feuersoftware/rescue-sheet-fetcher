using Rettungskarten.Core.Models;
using Rettungskarten.Core.Reporting;

namespace Rettungskarten.Tests.Reporting;

public class PriorityReportCsvFormatterTests
{
    private static RescueCardMetadata Card(string modelName, string? variant, int? fleetSize, BundlePriority priority) =>
        new(
            Id: $"test-{modelName}", Brand: Brand.VW, ModelName: modelName, Variant: variant, BodyType: null,
            BuildYearFrom: null, BuildYearTo: null, Doors: null, FuelType: null, LanguageCode: "DE",
            Status: RescueCardStatus.Downloaded, SourcePageUrl: "https://example.test", DownloadUrl: null,
            FailureReason: null, ParseConfidence: ParseConfidence.High, DiscoveredAtUtc: DateTimeOffset.UtcNow,
            DownloadedAtUtc: null, LocalPdfRelativePath: null, SiblingModelIds: [],
            EstimatedFleetSize: fleetSize, BundlePriority: priority);

    [Fact]
    public void Format_IncludesHeaderAndOneRowPerCard()
    {
        var csv = PriorityReportCsvFormatter.Format([Card("Golf", "Hatchback", 3_231_990, BundlePriority.High)]);

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Brand,ModelName,Variant,Status,EstimatedFleetSize,BundlePriority", lines[0]);
        Assert.Equal("VW,Golf,Hatchback,Downloaded,3231990,High", lines[1]);
    }

    [Fact]
    public void Format_VariantWithComma_IsQuoted()
    {
        var csv = PriorityReportCsvFormatter.Format([Card("Kodiaq", "Škoda Kodiaq, Kodiaq RS (ab 2021)", 174_768, BundlePriority.High)]);

        Assert.Contains("\"Škoda Kodiaq, Kodiaq RS (ab 2021)\"", csv);
    }

    [Fact]
    public void Format_VariantWithQuote_IsEscapedByDoubling()
    {
        var csv = PriorityReportCsvFormatter.Format([Card("Test", "Say \"hello\"", null, BundlePriority.Unknown)]);

        Assert.Contains("\"Say \"\"hello\"\"\"", csv);
    }

    [Fact]
    public void Format_NullVariantAndFleetSize_RenderAsEmptyField()
    {
        var csv = PriorityReportCsvFormatter.Format([Card("Test", null, null, BundlePriority.Unknown)]);

        var dataLine = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[1];
        Assert.Equal("VW,Test,,Downloaded,,Unknown", dataLine);
    }

    [Fact]
    public void Format_ResultParsesBackToTheSameNumberOfColumnsPerRow()
    {
        var cards = new[]
        {
            Card("Golf", "Hatchback, 5d", 3_231_990, BundlePriority.High),
            Card("Up", null, 15_000, BundlePriority.Low),
        };

        var csv = PriorityReportCsvFormatter.Format(cards);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length); // header + 2 rows
    }
}
