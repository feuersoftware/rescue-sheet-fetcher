using Rettungskarten.Core.Models;

namespace Rettungskarten.Tests.Models;

public class BrandRunResultTests
{
    private static readonly ParsedModelInfo Parsed = new(
        ModelName: "Golf", Variant: null, BodyType: null, BuildYearFrom: null, BuildYearTo: null,
        Doors: null, FuelType: null, LanguageCode: "DE", ParseConfidence: ParseConfidence.High);

    private static ModelRunResult Model(RescueCardStatus status, string? failureReason = null) => new(
        new RescueCardEntry(Brand.VW, "https://example.test", "https://example.test/a.pdf", "a.pdf", Parsed),
        status, failureReason);

    [Fact]
    public void Counts_DistinguishAllThreeStatusesSeparately()
    {
        // Regression test: Downloaded/MetadataOnly/Failed must each reflect their own RescueCardStatus,
        // not collapse "no download URL ever found" (Failed) and "URL found but the fetch failed"
        // (MetadataOnly) into the same number.
        var result = BrandRunResult.Completed(Brand.VW,
        [
            Model(RescueCardStatus.Downloaded),
            Model(RescueCardStatus.Downloaded),
            Model(RescueCardStatus.MetadataOnly, "HTTP 403"),
            Model(RescueCardStatus.Failed, "no download URL found"),
            Model(RescueCardStatus.Failed, "no download URL found"),
            Model(RescueCardStatus.Failed, "no download URL found"),
        ]);

        Assert.Equal(6, result.Discovered);
        Assert.Equal(2, result.Downloaded);
        Assert.Equal(1, result.MetadataOnly);
        Assert.Equal(3, result.Failed);
    }
}
