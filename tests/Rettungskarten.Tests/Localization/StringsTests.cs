using System.Globalization;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;

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
    /// <summary>DisplayText.For(ManufacturerGroup) builds its key from the enum name, so a new group
    /// without resource entries would print the raw key - checked for both cultures, since a key
    /// missing only from Strings.de.resx silently falls back to English.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EveryManufacturerGroup_HasADisplayName(string culture)
    {
        Strings.OverrideCulture = new CultureInfo(culture);

        foreach (var group in Enum.GetValues<ManufacturerGroup>())
        {
            Assert.NotNull(Strings.TryGet($"Group_{group}"));
        }
    }

    [Fact]
    public void GermanResources_HaveEveryNeutralKey()
    {
        var neutral = ReadKeys("Strings.resx");
        var german = ReadKeys("Strings.de.resx");

        Assert.Empty(neutral.Except(german));
        Assert.Empty(german.Except(neutral));
    }

    private static HashSet<string> ReadKeys(string fileName)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "Rettungskarten.Core", "Localization", fileName);
        return System.Xml.Linq.XDocument.Load(path).Root!.Elements("data")
            .Select(e => e.Attribute("name")!.Value)
            .ToHashSet();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rettungskarten.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}
