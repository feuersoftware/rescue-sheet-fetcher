using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Tests.RescueCards;

public class ModelYearRangeTextHelperTests
{
    [Theory]
    [InlineData("Škoda Kodiaq, Kodiaq RS (2016-2021)", 2016, 2021)]
    [InlineData("Škoda Kodiaq, Kodiaq RS (ab 2021)", 2021, null)]
    [InlineData("Škoda Fabia I (bis 2007)", null, 2007)]
    [InlineData("Škoda Kodiaq SUV 2024 5d GD", 2024, 2024)]
    [InlineData("kein Jahr enthalten", null, null)]
    public void Extract_ParsesExpectedRange(string text, int? expectedFrom, int? expectedTo)
    {
        var range = ModelYearRangeTextHelper.Extract(text);

        Assert.Equal(expectedFrom, range.From);
        Assert.Equal(expectedTo, range.To);
    }
}
