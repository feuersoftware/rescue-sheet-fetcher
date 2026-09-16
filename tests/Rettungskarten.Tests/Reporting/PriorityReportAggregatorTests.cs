using Rettungskarten.Core.Models;
using Rettungskarten.Core.Reporting;

namespace Rettungskarten.Tests.Reporting;

public class PriorityReportAggregatorTests
{
    private static RescueCardMetadata Card(
        string id, Brand brand, string? modelName, int? fleetSize, BundlePriority priority,
        RescueCardStatus status = RescueCardStatus.Downloaded) =>
        new(
            Id: id, Brand: brand, ModelName: modelName, Variant: $"variant-{id}", BodyType: null,
            BuildYearFrom: null, BuildYearTo: null, Doors: null, FuelType: null, LanguageCode: "DE",
            Status: status, SourcePageUrl: "https://example.test", DownloadUrl: null,
            FailureReason: null, ParseConfidence: ParseConfidence.High, DiscoveredAtUtc: DateTimeOffset.UtcNow,
            DownloadedAtUtc: null, LocalPdfRelativePath: null, SiblingModelIds: [],
            EstimatedFleetSize: fleetSize, BundlePriority: priority);

    [Fact]
    public void Aggregate_CollapsesMultipleVariantsOfSameModelIntoOneRow()
    {
        // Regression test for the reported problem: "Golf" appearing many times in the report with
        // identical fleet-size/priority, once per model-year/body-type variant.
        var cards = new[]
        {
            Card("vw-golf-1", Brand.VW, "Golf", 3_231_990, BundlePriority.High),
            Card("vw-golf-2", Brand.VW, "Golf", 3_231_990, BundlePriority.High),
            Card("vw-golf-3", Brand.VW, "Golf", 3_231_990, BundlePriority.High),
        };

        var rows = PriorityReportAggregator.Aggregate(cards);

        var row = Assert.Single(rows);
        Assert.Equal("Golf", row.ModelName);
        Assert.Equal(3, row.CardCount);
        Assert.Equal(0, row.NotDownloadedCount);
        Assert.Equal(3_231_990, row.EstimatedFleetSize);
        Assert.Equal(BundlePriority.High, row.BundlePriority);
    }

    [Fact]
    public void Aggregate_KeepsDifferentModelsAsSeparateRows()
    {
        var cards = new[]
        {
            Card("vw-golf-1", Brand.VW, "Golf", 3_231_990, BundlePriority.High),
            Card("vw-up-1", Brand.VW, "Up", 15_000, BundlePriority.Low),
        };

        var rows = PriorityReportAggregator.Aggregate(cards);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Aggregate_SameModelNameDifferentBrand_StaysSeparate()
    {
        var cards = new[]
        {
            Card("skoda-octavia-1", Brand.Skoda, "Octavia", 724_473, BundlePriority.High),
            Card("audi-octavia-lookalike-1", Brand.Audi, "Octavia", null, BundlePriority.Unknown),
        };

        var rows = PriorityReportAggregator.Aggregate(cards);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Aggregate_SortsByFleetSizeDescending_ThenByModelName()
    {
        var cards = new[]
        {
            Card("vw-up-1", Brand.VW, "Up", 15_000, BundlePriority.Low),
            Card("vw-golf-1", Brand.VW, "Golf", 3_231_990, BundlePriority.High),
            Card("vw-unknown-1", Brand.VW, "Unmatched", null, BundlePriority.Unknown),
        };

        var rows = PriorityReportAggregator.Aggregate(cards);

        Assert.Equal(["Golf", "Up", "Unmatched"], rows.Select(r => r.ModelName));
    }

    [Fact]
    public void Aggregate_CountsCardsThatArentDownloaded()
    {
        // A "High priority" model isn't fully covered on disk if some of its variants failed to
        // download - this must stay visible even after collapsing to one row per model.
        var cards = new[]
        {
            Card("vw-golf-1", Brand.VW, "Golf", 3_231_990, BundlePriority.High),
            Card("vw-golf-2", Brand.VW, "Golf", 3_231_990, BundlePriority.High, RescueCardStatus.MetadataOnly),
            Card("vw-golf-3", Brand.VW, "Golf", 3_231_990, BundlePriority.High, RescueCardStatus.Failed),
        };

        var row = Assert.Single(PriorityReportAggregator.Aggregate(cards));

        Assert.Equal(3, row.CardCount);
        Assert.Equal(2, row.NotDownloadedCount);
    }

    [Fact]
    public void Aggregate_CardsForSameModelDisagreeOnPriority_Throws()
    {
        // Shouldn't happen given how BundlePriorityCalculator works (see PriorityReportAggregator's
        // doc comment), but if it ever did, silently picking one card's values over another's would
        // produce a wrong report nobody notices is wrong - this must fail loudly instead.
        var cards = new[]
        {
            Card("vw-golf-1", Brand.VW, "Golf", 3_231_990, BundlePriority.High),
            Card("vw-golf-2", Brand.VW, "Golf", 15_000, BundlePriority.Low),
        };

        Assert.Throws<InvalidOperationException>(() => PriorityReportAggregator.Aggregate(cards));
    }
}
