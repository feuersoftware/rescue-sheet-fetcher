using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>Fixture (mg_page.html): five real caption+button blocks from mgmotor.de's rescue-card page
/// and an unrelated PDF link outside the download blocks.</summary>
public sealed class MgRescueCardSourceTests
{
    private const string PageUrl = "https://www.mgmotor.de/owners/rettungskarten";

    [Fact]
    public async Task DiscoverAsync_UsesCaptionsAsLabels()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "mg_page.html"));
        var factory = new StubHttpClientFactory().Html(PageUrl, html);

        var entries = await new MgRescueCardSource(factory, NullLogger<MgRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        Assert.Equal(["EHS", "MG3", "MG4", "MG4", "Marvel R"], entries.Select(e => e.Parsed.ModelName!).Order(StringComparer.Ordinal));

        var mg3 = Assert.Single(entries, e => e.Parsed.ModelName == "MG3");
        Assert.Equal("Hybrid", mg3.Parsed.FuelType);
        Assert.Null(mg3.Parsed.BuildYearFrom);

        var mg4 = Assert.Single(entries, e => e.Parsed.ModelName == "MG4" && e.Parsed.BuildYearFrom == 2023); // "MY-23" in the filename
        Assert.Equal("EN", mg4.Parsed.LanguageCode); // verified English edition, see the source
        Assert.Equal("DE", mg3.Parsed.LanguageCode);
        var urban = Assert.Single(entries, e => e.Parsed.ModelName == "MG4" && e.Parsed.BuildYearFrom == 2025);
        Assert.Equal("Hatchback", urban.Parsed.BodyType);

        Assert.Equal("PHEV", Assert.Single(entries, e => e.Parsed.ModelName == "EHS").Parsed.FuelType);
    }
}
