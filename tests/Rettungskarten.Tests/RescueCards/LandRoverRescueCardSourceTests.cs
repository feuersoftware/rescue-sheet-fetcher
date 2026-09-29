using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>Fixture (landrover_page.html): six real links from landrover.de's emergency page (link text
/// = years only, model in the aria-label) plus a catalogue PDF outside the rescue-sheet folder.</summary>
public sealed class LandRoverRescueCardSourceTests
{
    private const string PageUrl = "https://www.landrover.de/ownership/emergency.html";

    [Fact]
    public async Task DiscoverAsync_ParsesAriaLabelsAndGenerationCodes()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "landrover_page.html"));
        var factory = new StubHttpClientFactory().Html(PageUrl, html);

        var entries = await new LandRoverRescueCardSource(factory, NullLogger<LandRoverRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        Assert.Equal(6, entries.Count);

        var sport = Assert.Single(entries, e => e.Parsed.ChassisCode == "LW");
        Assert.Equal("Range Rover Sport", sport.Parsed.ModelName);
        Assert.Equal(2013, sport.Parsed.BuildYearFrom);
        Assert.Equal(2022, sport.Parsed.BuildYearTo);

        var sportPhev = Assert.Single(entries, e => e.Parsed.ChassisCode == "L1");
        Assert.Equal("Range Rover Sport", sportPhev.Parsed.ModelName);
        Assert.Equal("PHEV", sportPhev.Parsed.FuelType);
        Assert.Equal(2022, sportPhev.Parsed.BuildYearFrom);
        Assert.Null(sportPhev.Parsed.BuildYearTo);

        var defender = Assert.Single(entries, e => e.Parsed.ModelName == "Defender");
        Assert.Equal("MHEV", defender.Parsed.FuelType);

        var evoque = Assert.Single(entries, e => e.Parsed.ModelName == "Range Rover Evoque");
        Assert.Equal(5, evoque.Parsed.Doors);

        Assert.Single(entries, e => e.Parsed.ModelName == "Freelander" && e.Parsed.ChassisCode == "LF");
        Assert.Single(entries, e => e.Parsed.ModelName == "Discovery" && e.Parsed.ChassisCode == "LR");
    }
}
