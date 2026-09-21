using System.Globalization;
using Rettungskarten.Core.Localization;

namespace Rettungskarten.Tests.Localization;

/// <summary>
/// Strings.OverrideCulture is process-global static state, so every test here resets it in Dispose -
/// otherwise a leaked override could make unrelated tests running in parallel flaky.
/// </summary>
public class StringsTests : IDisposable
{
    public void Dispose() => Strings.OverrideCulture = null;

    [Fact]
    public void Get_EnglishCulture_ReturnsNeutralResource()
    {
        Strings.OverrideCulture = new CultureInfo("en");

        Assert.Equal("Brand", Strings.Get("Summary_Column_Brand"));
    }

    [Fact]
    public void Get_GermanCulture_ReturnsGermanResource()
    {
        Strings.OverrideCulture = new CultureInfo("de");

        Assert.Equal("Marke", Strings.Get("Summary_Column_Brand"));
    }

    [Fact]
    public void Get_WithArgs_FormatsUsingResourceString()
    {
        Strings.OverrideCulture = new CultureInfo("en");

        Assert.Equal("Starting Skoda...", Strings.Get("Log_StartingBrand", "Skoda"));
    }

    [Fact]
    public void ParseLanguageOption_UnknownValue_ReturnsNull()
    {
        Assert.Null(Strings.ParseLanguageOption("fr"));
        Assert.Null(Strings.ParseLanguageOption(null));
    }

    [Fact]
    public void ParseLanguageOption_KnownValues_ReturnsCulture()
    {
        Assert.Equal("de", Strings.ParseLanguageOption("de")!.TwoLetterISOLanguageName);
        Assert.Equal("en", Strings.ParseLanguageOption("EN")!.TwoLetterISOLanguageName);
    }
}
