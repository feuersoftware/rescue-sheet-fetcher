using Rettungskarten.Core.Models;
using Rettungskarten.Core.Quality;

namespace Rettungskarten.Tests.Quality;

public class DataQualityCheckerTests
{
    private static RescueCardMetadata Card(
        string id, Brand brand = Brand.Audi, string? bodyType = null, string? fuelType = null,
        BundlePriority priority = BundlePriority.Unknown) =>
        new(
            Id: id, Brand: brand, ModelName: "Test", Variant: null, BodyType: bodyType,
            BuildYearFrom: null, BuildYearTo: null, Doors: null, FuelType: fuelType,
            LanguageCode: "DE", Status: RescueCardStatus.Downloaded, SourcePageUrl: "https://example.test",
            DownloadUrl: null, FailureReason: null, ParseConfidence: ParseConfidence.Heuristic,
            DiscoveredAtUtc: DateTimeOffset.UtcNow, DownloadedAtUtc: null, LocalPdfRelativePath: null,
            SiblingModelIds: [], EstimatedFleetSize: null, BundlePriority: priority);

    [Fact]
    public void CheckAll_BodyTypeIsAYear_ReportsIssue()
    {
        // Regression test for the real Audi bug this session found manually: a filename-parser
        // off-by-one shifted the model year into BodyType (e.g. "2018", "2019-2023").
        var cards = new[] { Card("audi-a6-1", bodyType: "2018") };

        var issues = DataQualityChecker.CheckAll(cards);

        var issue = Assert.Single(issues);
        Assert.Equal(DataQualityIssueKind.BodyTypeLooksLikeYear, issue.Kind);
        Assert.Equal("audi-a6-1", issue.CardId);
    }

    [Fact]
    public void CheckAll_BodyTypeIsAYearRange_ReportsIssue()
    {
        var cards = new[] { Card("audi-etron-1", bodyType: "2019-2023") };

        var issues = DataQualityChecker.CheckAll(cards);

        Assert.Single(issues, i => i.Kind == DataQualityIssueKind.BodyTypeLooksLikeYear);
    }

    [Fact]
    public void CheckAll_RealBodyType_NoIssue()
    {
        var cards = new[] { Card("audi-a6-2", bodyType: "Sedan") };

        var issues = DataQualityChecker.CheckAll(cards);

        Assert.Empty(issues);
    }

    [Fact]
    public void CheckAll_FuelTypeIsBareDigits_ReportsIssue()
    {
        var cards = new[] { Card("audi-etron-2", fuelType: "1") };

        var issues = DataQualityChecker.CheckAll(cards);

        var issue = Assert.Single(issues);
        Assert.Equal(DataQualityIssueKind.FuelTypeIsBareDigits, issue.Kind);
    }

    [Fact]
    public void CheckAll_RealFuelType_NoIssue()
    {
        var cards = new[] { Card("audi-etron-3", fuelType: "Electric") };

        var issues = DataQualityChecker.CheckAll(cards);

        Assert.Empty(issues);
    }

    [Fact]
    public void CheckAll_DuplicateId_ReportsIssue()
    {
        var cards = new[] { Card("vw-golf-1"), Card("vw-golf-1") };

        var issues = DataQualityChecker.CheckAll(cards);

        var issue = Assert.Single(issues);
        Assert.Equal(DataQualityIssueKind.DuplicateId, issue.Kind);
        Assert.Equal("vw-golf-1", issue.CardId);
    }

    [Fact]
    public void CheckAll_BrandEntirelyUnknown_WhileOtherBrandMatched_ReportsIssue()
    {
        // Regression test for the shape of a real bug this session found: every Cupra card had
        // BundlePriority.Unknown because KBA tracks Cupra models under "SEAT", not "CUPRA" - a
        // brand-matching gap, not genuinely 100% untracked models (proven by VW matching fine).
        var cards = new[]
        {
            Card("cupra-leon-1", brand: Brand.Cupra, priority: BundlePriority.Unknown),
            Card("cupra-ateca-1", brand: Brand.Cupra, priority: BundlePriority.Unknown),
            Card("vw-golf-1", brand: Brand.VW, priority: BundlePriority.High),
        };

        var issues = DataQualityChecker.CheckAll(cards);

        var issue = Assert.Single(issues);
        Assert.Equal(DataQualityIssueKind.BrandEntirelyUnknownPriority, issue.Kind);
        Assert.Equal(Brand.Cupra, issue.Brand);
    }

    [Fact]
    public void CheckAll_AllBrandsUnknown_PrioritizeNeverRan_NoIssue()
    {
        // If nothing in the whole dataset has a priority yet, `prioritize` simply hasn't run - that's
        // not itself a sign of a brand-matching bug, unlike one brand being the only holdout.
        var cards = new[]
        {
            Card("cupra-leon-1", brand: Brand.Cupra, priority: BundlePriority.Unknown),
            Card("vw-golf-1", brand: Brand.VW, priority: BundlePriority.Unknown),
        };

        var issues = DataQualityChecker.CheckAll(cards);

        Assert.Empty(issues);
    }

    [Fact]
    public void CheckAll_PorscheUnknownCountExceedsBaseline_ReportsIssue()
    {
        // Porsche's ultra-low-volume specials (GT2/GT3/Turbo/...) are deliberately left unmatched
        // (see commit b19a397: 65/85 -> 19/85 Unknown), so 19 Unknown is expected. If that count grows
        // - a new unmatched model, or a broken alias - this should surface instead of blending in.
        var cards = new List<RescueCardMetadata>
        {
            Card("vw-golf-1", brand: Brand.VW, priority: BundlePriority.High)
        };
        for (var i = 0; i < 66; i++)
        {
            cards.Add(Card($"porsche-matched-{i}", brand: Brand.Porsche, priority: BundlePriority.Medium));
        }
        for (var i = 0; i < 20; i++)
        {
            cards.Add(Card($"porsche-unknown-{i}", brand: Brand.Porsche, priority: BundlePriority.Unknown));
        }

        var issues = DataQualityChecker.CheckAll(cards);

        var issue = Assert.Single(issues);
        Assert.Equal(DataQualityIssueKind.BrandUnknownPriorityCountAboveBaseline, issue.Kind);
        Assert.Equal(Brand.Porsche, issue.Brand);
    }

    [Fact]
    public void CheckAll_PorscheUnknownCountAtBaseline_NoIssue()
    {
        var cards = new List<RescueCardMetadata>
        {
            Card("vw-golf-1", brand: Brand.VW, priority: BundlePriority.High)
        };
        for (var i = 0; i < 66; i++)
        {
            cards.Add(Card($"porsche-matched-{i}", brand: Brand.Porsche, priority: BundlePriority.Medium));
        }
        for (var i = 0; i < 19; i++)
        {
            cards.Add(Card($"porsche-unknown-{i}", brand: Brand.Porsche, priority: BundlePriority.Unknown));
        }

        var issues = DataQualityChecker.CheckAll(cards);

        Assert.Empty(issues);
    }

    [Fact]
    public void CheckAll_BrandWithoutBaseline_ManyUnknownButNotAll_NoIssue()
    {
        // No baseline is configured for VW, so a partial-Unknown mix (unlike Porsche) doesn't trigger
        // BrandUnknownPriorityCountAboveBaseline - only brands with a known, deliberate gap do.
        var cards = new List<RescueCardMetadata>
        {
            Card("vw-golf-1", brand: Brand.VW, priority: BundlePriority.High)
        };
        for (var i = 0; i < 30; i++)
        {
            cards.Add(Card($"vw-unknown-{i}", brand: Brand.VW, priority: BundlePriority.Unknown));
        }

        var issues = DataQualityChecker.CheckAll(cards);

        Assert.Empty(issues);
    }

    [Fact]
    public void CheckAll_NoAnomalies_ReturnsEmpty()
    {
        var cards = new[] { Card("vw-golf-1", bodyType: "Hatchback", fuelType: "GD"), Card("vw-golf-2", bodyType: "Sedan") };

        var issues = DataQualityChecker.CheckAll(cards);

        Assert.Empty(issues);
    }
}
