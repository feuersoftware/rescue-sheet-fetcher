using Rettungskarten.Core.Config;
using Rettungskarten.Core.Models;
using Rettungskarten.Core.Quality;

namespace Rettungskarten.Tests.Quality;

public class DataQualityCheckerTests
{
    private static RescueCardMetadata Card(
        string id, Brand brand = Brand.Audi, string? bodyType = null, string? fuelType = null,
        BundlePriority priority = BundlePriority.Unknown, DocumentScope scope = DocumentScope.Single,
        string modelName = "Test") =>
        new(
            Id: id, Brand: brand, ModelName: modelName, Variant: null, BodyType: bodyType,
            BuildYearFrom: null, BuildYearTo: null, Doors: null, FuelType: fuelType,
            LanguageCode: "DE", Status: RescueCardStatus.Downloaded, SourcePageUrl: "https://example.test",
            DownloadUrl: null, FailureReason: null, ParseConfidence: ParseConfidence.Heuristic,
            DiscoveredAtUtc: DateTimeOffset.UtcNow, DownloadedAtUtc: null, LocalPdfRelativePath: null,
            SiblingModelIds: [], EstimatedFleetSize: null, BundlePriority: priority, DocumentScope: scope);

    [Fact]
    public void CheckAll_BodyTypeIsAYear_ReportsIssue()
    {
        // Regression test for the real Audi bug this session found manually: a filename-parser
        // off-by-one shifted the model year into BodyType (e.g. "2018", "2019-2023").
        var cards = new[] { Card("audi-a6-1", bodyType: "2018") };

        var issues = DataQualityChecker.CheckAll(cards, KbaUnlistedModelConfig.Empty);

        var issue = Assert.Single(issues);
        Assert.Equal(DataQualityIssueKind.BodyTypeLooksLikeYear, issue.Kind);
        Assert.Equal("audi-a6-1", issue.CardId);
    }

    [Fact]
    public void CheckAll_BodyTypeIsAYearRange_ReportsIssue()
    {
        var cards = new[] { Card("audi-etron-1", bodyType: "2019-2023") };

        var issues = DataQualityChecker.CheckAll(cards, KbaUnlistedModelConfig.Empty);

        Assert.Single(issues, i => i.Kind == DataQualityIssueKind.BodyTypeLooksLikeYear);
    }

    [Fact]
    public void CheckAll_RealBodyType_NoIssue()
    {
        var cards = new[] { Card("audi-a6-2", bodyType: "Sedan") };

        var issues = DataQualityChecker.CheckAll(cards, KbaUnlistedModelConfig.Empty);

        Assert.Empty(issues);
    }

    [Fact]
    public void CheckAll_FuelTypeIsBareDigits_ReportsIssue()
    {
        var cards = new[] { Card("audi-etron-2", fuelType: "1") };

        var issues = DataQualityChecker.CheckAll(cards, KbaUnlistedModelConfig.Empty);

        var issue = Assert.Single(issues);
        Assert.Equal(DataQualityIssueKind.FuelTypeIsBareDigits, issue.Kind);
    }

    [Fact]
    public void CheckAll_RealFuelType_NoIssue()
    {
        var cards = new[] { Card("audi-etron-3", fuelType: "Electric") };

        var issues = DataQualityChecker.CheckAll(cards, KbaUnlistedModelConfig.Empty);

        Assert.Empty(issues);
    }

    [Fact]
    public void CheckAll_DuplicateId_ReportsIssue()
    {
        var cards = new[] { Card("vw-golf-1"), Card("vw-golf-1") };

        var issues = DataQualityChecker.CheckAll(cards, KbaUnlistedModelConfig.Empty);

        var issue = Assert.Single(issues);
        Assert.Equal(DataQualityIssueKind.DuplicateId, issue.Kind);
        Assert.Equal("vw-golf-1", issue.CardId);
    }

    private static readonly KbaUnlistedModelConfig Unlisted = new(
    [
        new KbaUnlistedModel(Brand.Porsche, "911 GT3", "special"),
        new KbaUnlistedModel(Brand.RollsRoyce, ModelAliasConfig.AnyModel, "brand not in FZ12")
    ]);

    [Fact]
    public void CheckAll_UnmatchedModelNotListed_ReportsOneErrorPerModel()
    {
        // The shape of real bugs this caught: every Cupra card stayed Unknown because KBA counts Cupra
        // under "SEAT", and Porsche split parts named by their document id instead of the model.
        var cards = new[]
        {
            Card("cupra-leon-1", brand: Brand.Cupra, modelName: "Leon"),
            Card("cupra-leon-2", brand: Brand.Cupra, modelName: "LEON"),
            Card("vw-golf-1", brand: Brand.VW, priority: BundlePriority.High, modelName: "Golf"),
        };

        var issue = Assert.Single(DataQualityChecker.CheckAll(cards, Unlisted));

        Assert.Equal(DataQualityIssueKind.UnmatchedModel, issue.Kind);
        Assert.Equal(DataQualityIssueSeverity.Error, issue.Severity);
        Assert.Equal(Brand.Cupra, issue.Brand);
    }

    [Fact]
    public void CheckAll_UnmatchedModelListed_NoIssue()
    {
        var cards = new[]
        {
            Card("vw-golf-1", brand: Brand.VW, priority: BundlePriority.High, modelName: "Golf"),
            Card("porsche-gt3-1", brand: Brand.Porsche, modelName: "911 GT3"),
            Card("rr-ghost-1", brand: Brand.RollsRoyce, modelName: "Ghost"),
        };

        Assert.Empty(DataQualityChecker.CheckAll(cards, Unlisted));
    }

    [Fact]
    public void CheckAll_ListedModelMatchedAfterAll_ReportsWarning()
    {
        // A new FZ12 edition may start listing a model - the flag should then be reviewed, not
        // silently kept, but that's no reason to fail the run.
        var cards = new[] { Card("porsche-gt3-1", brand: Brand.Porsche, priority: BundlePriority.Low, modelName: "911 GT3") };

        var issue = Assert.Single(DataQualityChecker.CheckAll(cards, Unlisted));

        Assert.Equal(DataQualityIssueKind.UnlistedModelMatched, issue.Kind);
        Assert.Equal(DataQualityIssueSeverity.Warning, issue.Severity);
    }

    [Fact]
    public void CheckAll_AllUnknown_PrioritizeNeverRan_NoIssue()
    {
        // If nothing in the whole dataset has a priority yet, `prioritize` simply hasn't run.
        var cards = new[]
        {
            Card("cupra-leon-1", brand: Brand.Cupra, modelName: "Leon"),
            Card("vw-golf-1", brand: Brand.VW, modelName: "Golf"),
        };

        Assert.Empty(DataQualityChecker.CheckAll(cards, Unlisted));
    }

    [Fact]
    public void CheckAll_NoAnomalies_ReturnsEmpty()
    {
        var cards = new[] { Card("vw-golf-1", bodyType: "Hatchback", fuelType: "GD"), Card("vw-golf-2", bodyType: "Sedan") };

        var issues = DataQualityChecker.CheckAll(cards, KbaUnlistedModelConfig.Empty);

        Assert.Empty(issues);
    }
    [Fact]
    public void CheckAll_CombinedDocuments_DoNotCountAsUnmatchedCards()
    {
        // A combined all-models PDF has no single model name; its split parts are what gets matched.
        var cards = new[]
        {
            Card("vw-1", Brand.VW, priority: BundlePriority.High),
            Card("ford-all", Brand.Ford, scope: DocumentScope.Combined, modelName: "All Models"),
            Card("ford-kuga", Brand.Ford, priority: BundlePriority.High, scope: DocumentScope.SplitPart, modelName: "Kuga")
        };

        Assert.Empty(DataQualityChecker.CheckAll(cards, KbaUnlistedModelConfig.Empty));
    }
}
