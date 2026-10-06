using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Tests.RescueCards;

public class ModelYearRangeTextHelperTests
{
    [Theory]
    [InlineData("Škoda Kodiaq, Kodiaq RS (2016-2021)", 2016, 2021)]
    [InlineData("Škoda Kodiaq, Kodiaq RS (ab 2021)", 2021, null)]
    [InlineData("Škoda Fabia I (bis 2007)", null, 2007)]
    [InlineData("Škoda Kodiaq SUV 2024 5d GD", 2024, 2024)]
    [InlineData("BENTAYGA (HYBRID) (2021 - )", 2021, null)]
    [InlineData("NEW CONTINENTAL GT (ICE) (2018 - 2024)", 2018, 2024)]
    [InlineData("kein Jahr enthalten", null, null)]
    public void Extract_ParsesExpectedRange(string text, int? expectedFrom, int? expectedTo)
    {
        var range = ModelYearRangeTextHelper.Extract(text);

        Assert.Equal(expectedFrom, range.From);
        Assert.Equal(expectedTo, range.To);
    }

    [Theory]
    // Regression: a range spelled out in words used to keep only one end ("von X bis Y" -> (null, Y),
    // "ab X bis Y" -> (X, null)); Mazda and Renault each worked around it privately.
    [InlineData("von 2012 bis 2018", 2012, 2018)]
    [InlineData("Mazda MX-30 (DR) von 2020 bis 2022", 2020, 2022)]
    [InlineData("3er-Reihe E90 (ab 03/2005 bis 09/2011)", 2005, 2011)]
    [InlineData("von 03/2012 bis 11/2018", 2012, 2018)]
    [InlineData("Rettungsdatenblatt Sandero, 2008 bis 2012", 2008, 2012)]
    [InlineData("from 2019 to 2023", 2019, 2023)]
    [InlineData("from Model Year 2011 to 2013", 2011, 2013)]
    [InlineData("Model Year 2003 to Model Year 2005", 2003, 2005)]
    [InlineData("ab 2019", 2019, null)]
    [InlineData("bis 2020", null, 2020)]
    public void Extract_WordedRange_KeepsBothEnds(string text, int? expectedFrom, int? expectedTo)
    {
        var range = ModelYearRangeTextHelper.Extract(text, singleYearIsStartYear: true);

        Assert.Equal(expectedFrom, range.From);
        Assert.Equal(expectedTo, range.To);
    }
}
