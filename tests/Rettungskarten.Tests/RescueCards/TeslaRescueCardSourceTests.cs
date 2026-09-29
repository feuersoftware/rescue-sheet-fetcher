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
        Assert.Single(entries, e => e.Parsed.ModelName == "Roadster");
    }
}
