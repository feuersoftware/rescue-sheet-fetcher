using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Tests.RescueCards;

public class VehicleAttributeTextHelperTests
{
    private static readonly string[] BodyTypeVocabulary = ["Cabriolet", "Coupé", "SUV", "Spyder"];

    [Fact]
    public void ExtractLastVocabularyMatch_PrefersLaterMatchOverModelNameToken()
    {
        // "Spyder" appears twice here: once as part of the model name itself ("Boxster Spyder"), once
        // nowhere else - so this asserts the match a caller actually wants (the real body type,
        // "Cabriolet") isn't preempted by an earlier vocabulary word that's really part of the name.
        var result = VehicleAttributeTextHelper.ExtractLastVocabularyMatch(
            "Boxster Spyder (981) Cabriolet from Model Year 2015", BodyTypeVocabulary);

        Assert.Equal("Cabriolet", result);
    }

    [Fact]
    public void ExtractLastVocabularyMatch_NoVocabularyWord_ReturnsNull()
    {
        var result = VehicleAttributeTextHelper.ExtractLastVocabularyMatch("356 Urmodell from Model Year 1948", BodyTypeVocabulary);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("Škoda Citigo 3-Türer (ab 2011)", 3)]
    [InlineData("Škoda Citigo 5-Türer CNG (ab 2012)", 5)]
    [InlineData("Škoda Kodiaq SUV 2024 5d GD", 5)]
    [InlineData("Škoda Enyaq (ab 2023)", null)]
    public void ExtractDoors_ParsesBothConventions(string text, int? expectedDoors)
    {
        Assert.Equal(expectedDoors, VehicleAttributeTextHelper.ExtractDoors(text));
    }
}
