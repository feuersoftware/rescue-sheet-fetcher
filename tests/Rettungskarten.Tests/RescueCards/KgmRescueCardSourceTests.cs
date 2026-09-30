using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>Fixture (kgm_page.html): the real rescue-sheet table from kgm.de (all 19 rows - link text
/// is only "hier", the model and year are in the row's other cells) plus a navigation link.</summary>
public sealed class KgmRescueCardSourceTests
{
    private const string PageUrl = "https://www.kgm.de/rettungsdatenblaetter";

    [Fact]
    public async Task DiscoverAsync_BuildsLabelsFromTableRows()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "kgm_page.html"));
        var factory = new StubHttpClientFactory().Html(PageUrl, html);

        var entries = await new KgmRescueCardSource(factory, NullLogger<KgmRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        Assert.Equal(19, entries.Count);

        var rexton = entries.Where(e => e.Parsed.ModelName == "Rexton").OrderBy(e => e.Parsed.BuildYearFrom).ToList();
        Assert.Equal([2013, 2016, 2019], rexton.Select(e => e.Parsed.BuildYearFrom!.Value));
        Assert.Equal("Y415", rexton[2].Parsed.ChassisCode);

        var tivoli = Assert.Single(entries, e => e.Parsed.Variant == "Tivoli X150");
        Assert.Equal("Tivoli", tivoli.Parsed.ModelName);
        Assert.Equal("X150", tivoli.Parsed.ChassisCode);

        var evxVan = Assert.Single(entries, e => e.Parsed.ChassisCode == "U105");
        Assert.Equal("Torres", evxVan.Parsed.ModelName);
        Assert.Equal("Elektro", evxVan.Parsed.FuelType);
        Assert.Equal("Van", evxVan.Parsed.BodyType);
        Assert.Equal("EN", evxVan.Parsed.LanguageCode); // verified English edition, see the source
        Assert.Equal("DE", tivoli.Parsed.LanguageCode);
        Assert.Equal(8, entries.Count(e => e.Parsed.LanguageCode == "EN"));

        var musso = Assert.Single(entries, e => e.Parsed.ChassisCode == "Q300");
        Assert.Equal("Musso", musso.Parsed.ModelName);
        Assert.Equal(2026, musso.Parsed.BuildYearFrom);

        Assert.Single(entries, e => e.Parsed.ModelName == "Actyon Sports" && e.Parsed.BodyType == "Pick-up");
        Assert.Single(entries, e => e.Parsed.ModelName == "Actyon" && e.Parsed.ChassisCode == "J120");
    }
}
