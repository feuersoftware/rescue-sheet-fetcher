using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>Fixture (tesla_page.html): eight real links from Tesla's German "Fahrzeuge und Laden"
/// first-responder page - two ERGs ("Notfall-Handbuch") and six rescue sheets, several of them
/// English-only behind a German label.</summary>
public sealed class TeslaRescueCardSourceTests
{
    private const string PageUrl = "https://www.tesla.com/de_DE/firstresponders/vehicles-charging";

    [Fact]
    public async Task DiscoverAsync_KeepsRescueSheetsOnly_WithFilenameLanguage()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "tesla_page.html"));
        var factory = new StubHttpClientFactory().Html(PageUrl, html);

        var entries = await new TeslaRescueCardSource(factory, NullLogger<TeslaRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        Assert.Equal(6, entries.Count);
        Assert.All(entries, e => Assert.Contains("rescue", e.RawFileNameOrLabel, StringComparison.OrdinalIgnoreCase));
        Assert.All(factory.Requests, r => Assert.Equal(RettungskartenHttpClient.BrowserName, r.ClientName));

        var modelS = entries.Where(e => e.Parsed.ModelName == "Model S").OrderBy(e => e.Parsed.BuildYearFrom).ToList();
        Assert.Equal([2016, 2021, 2022], modelS.Select(e => e.Parsed.BuildYearFrom!.Value));
        Assert.Equal(["DE", "EN", "EN"], modelS.Select(e => e.Parsed.LanguageCode!));
        Assert.Equal(2020, modelS[0].Parsed.BuildYearTo);
        Assert.Equal(2021, modelS[1].Parsed.BuildYearTo); // bare year = that one year
        Assert.Null(modelS[2].Parsed.BuildYearTo); // "2022+"

        var modelY = Assert.Single(entries, e => e.Parsed.ModelName == "Model Y");
        Assert.Equal("EN", modelY.Parsed.LanguageCode);
        Assert.Equal(2025, modelY.Parsed.BuildYearFrom);

        Assert.Single(entries, e => e.Parsed.ModelName == "Model 3" && e.Parsed.LanguageCode == "DE");

        // Regression: the label says "2010+", the file "2010-13" - the closed filename range wins.
        var roadster = Assert.Single(entries, e => e.Parsed.ModelName == "Roadster");
        Assert.Equal(2010, roadster.Parsed.BuildYearFrom);
        Assert.Equal(2013, roadster.Parsed.BuildYearTo);
    }

    [Theory]
    [InlineData("2010-13_Roadster_Rescue_Sheet_en", 2010, 2013)]
    [InlineData("2016-2020_Model_S_Rescue_Sheet_de", 2016, 2020)]
    [InlineData("1998-02_Some_Sheet_en", 1998, 2002)]
    public void FileNameYears_ReadsClosedRanges(string name, int from, int to) =>
        Assert.Equal(new Rettungskarten.Infrastructure.RescueCards.Parsing.YearRange(from, to),
            Rettungskarten.Infrastructure.RescueCards.Parsing.TeslaLabelParser.FileNameYears(name));

    [Theory]
    [InlineData("2022_Model_S_Rescue_Sheet_en_eu")]
    [InlineData("2025-Model-Y-Rescue-Sheet-EN")]
    [InlineData("2024_10_Model_3_Rescue_Sheet_de")] // a date, not a generation: would read as 2024-2110
    public void FileNameYears_IgnoresASingleYear(string name) =>
        Assert.Null(Rettungskarten.Infrastructure.RescueCards.Parsing.TeslaLabelParser.FileNameYears(name));
}
