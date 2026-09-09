using Rettungskarten.Infrastructure.RescueCards;

namespace Rettungskarten.Tests.RescueCards;

public class SkodaRescueCardSourceTests
{
    // Real model-page titles found during a production run. "iV" is Škoda's lower-case-i electric-trim
    // badge (e.g. "Citigo-e iV") - it must not be confused with the upper-case-I Roman-numeral
    // generation marker used throughout these same titles ("Fabia IV", "Octavia IV"), which is never a
    // fuel type on its own.
    [Theory]
    [InlineData("Škoda Fabia IV (ab 2021)", null)]
    [InlineData("Škoda Octavia III (ab 2012)", null)]
    [InlineData("Škoda Citigo-e iV (ab 2019)", "iV")]
    [InlineData("Škoda Octavia IV CNG (ab 2020)", "CNG")]
    [InlineData("Škoda Superb iV PHEV HYBRID (ab 2024)", "PHEV HYBRID")]
    public void ExtractFuelType_DistinguishesElectricTrimBadgeFromGenerationNumeral(string title, string? expectedFuelType)
    {
        Assert.Equal(expectedFuelType, SkodaRescueCardSource.ExtractFuelType(title));
    }
}
