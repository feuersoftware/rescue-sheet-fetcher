using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.RenaultGroup;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixtures are trimmed slices of the real renault.de / dacia.de rescue-card pages (real anchors,
/// surrounding tab markup removed): the Renault slice covers all three filename generations (standard,
/// damaged standard, old "renault_rettungsdatenblatt_*_2014"), the "Renault 5"/"Renault 19" numbered
/// model names, the Fluence Z.E. sheet linked three times, and the two footer legal PDFs that must be
/// ignored; the Dacia slice has title attributes carrying the years and its footer legal PDF.
/// </summary>
public sealed class RenaultGroupRescueCardSourceTests
{
    private const string RenaultPageUrl = "https://www.renault.de/tipps-und-anleitungen/rettungskarten.html";
    private const string DaciaPageUrl = "https://www.dacia.de/rettungskarten.html";

    private static async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(Brand brand, string pageUrl, string fixture)
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", fixture));
        var factory = new StubHttpClientFactory().Html(pageUrl, html);
        var source = new RenaultGroupRescueCardSource(brand, factory, NullLogger<RenaultGroupRescueCardSource>.Instance);
        return await source.DiscoverAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DiscoverAsync_Renault_KeepsOnlyRescueSheetFoldersAndDeduplicates()
    {
        var entries = await DiscoverAsync(Brand.Renault, RenaultPageUrl, "renault_page.html");

        // 11 distinct sheets + Fluence Z.E. (linked three times); warranty notice and terms dropped.
        Assert.Equal(12, entries.Count);
        Assert.All(entries, e => Assert.Equal(Brand.Renault, e.Brand));
        Assert.All(entries, e => Assert.Contains("/ren/de/rettungskarten/", e.DownloadUrl));
        Assert.All(entries, e => Assert.Equal("DE", e.Parsed.LanguageCode));
        Assert.Single(entries, e => e.Parsed.Variant == "FLUENCE ZE");
    }

    [Fact]
    public async Task DiscoverAsync_Renault_UsesRealFileNameNotCdnAssetIdAsRawName()
    {
        var entries = await DiscoverAsync(Brand.Renault, RenaultPageUrl, "renault_page.html");

        var austral = Assert.Single(entries, e => e.Parsed.Variant == "AUSTRAL MILD HYBRID");
        Assert.Equal("austral/Renault_Austral_12-48V_Hatchback_2022_5d_GD_DE.pdf", austral.RawFileNameOrLabel);
        Assert.EndsWith("/a037b0acd9.pdf", austral.DownloadUrl);
        Assert.Equal(entries.Count, entries.Select(e => e.RawFileNameOrLabel).Distinct().Count());
    }

    [Fact]
    public async Task DiscoverAsync_Renault_ParsesAllFilenameGenerations()
    {
        var entries = await DiscoverAsync(Brand.Renault, RenaultPageUrl, "renault_page.html");

        var austral = Assert.Single(entries, e => e.Parsed.Variant == "AUSTRAL MILD HYBRID");
        Assert.Equal("Austral", austral.Parsed.ModelName);
        Assert.Equal("Hatchback", austral.Parsed.BodyType);
        Assert.Equal(2022, austral.Parsed.BuildYearFrom);
        Assert.Equal(5, austral.Parsed.Doors);
        Assert.Equal("Mild Hybrid", austral.Parsed.FuelType); // label wins over the file's "GD"
        Assert.Equal(ParseConfidence.High, austral.Parsed.ParseConfidence);

        // Damaged convention ("Renault_Clio 5_2020_5d_LPG_DE") still yields year, doors and fuel.
        var clio5 = Assert.Single(entries, e => e.Parsed.Variant == "CLIO 5");
        Assert.Equal("Clio", clio5.Parsed.ModelName);
        Assert.Equal(2020, clio5.Parsed.BuildYearFrom);
        Assert.Equal("LPG", clio5.Parsed.FuelType);

        // Old scheme: "_2014" is the publication year and must not become a build year.
        var clio1 = Assert.Single(entries, e => e.Parsed.Variant == "CLIO 1");
        Assert.Null(clio1.Parsed.BuildYearFrom);
        Assert.Null(clio1.Parsed.BuildYearTo);

        // ...but an explicit "ab-2011" in an old name is trusted.
        var kangooZe = Assert.Single(entries, e => e.RawFileNameOrLabel.Contains("kangoo-ZE-ab-2011"));
        Assert.Equal("Kangoo", kangooZe.Parsed.ModelName);
        Assert.Equal(2011, kangooZe.Parsed.BuildYearFrom);
        Assert.Equal("Electric", kangooZe.Parsed.FuelType);

        var masterZe = Assert.Single(entries, e => e.Parsed.Variant == "MASTER E-TECH (ab 2019)");
        Assert.Equal(2019, masterZe.Parsed.BuildYearFrom);
    }

    [Fact]
    public async Task DiscoverAsync_Renault_KeepsNumberedAndTwoWordModelNames()
    {
        var entries = await DiscoverAsync(Brand.Renault, RenaultPageUrl, "renault_page.html");

        var names = entries.Select(e => e.Parsed.ModelName).ToHashSet();
        Assert.Contains("Renault 5", names);
        Assert.Contains("Renault 19", names);
        Assert.Contains("Grand Scenic", names);
    }

    [Fact]
    public async Task DiscoverAsync_Dacia_ReadsYearsFromTitleAttribute()
    {
        var entries = await DiscoverAsync(Brand.Dacia, DaciaPageUrl, "dacia_page.html");

        Assert.Equal(6, entries.Count); // connected-services terms dropped
        Assert.All(entries, e => Assert.Equal(Brand.Dacia, e.Brand));

        // title="Rettungsdatenblatt Sandero, 2008 bis 2012"
        var sandero1 = Assert.Single(entries, e => e.Parsed.Variant == "Sandero 1");
        Assert.Equal("Sandero", sandero1.Parsed.ModelName);
        Assert.Equal(2008, sandero1.Parsed.BuildYearFrom);
        Assert.Equal(2012, sandero1.Parsed.BuildYearTo);

        var lodgy = Assert.Single(entries, e => e.Parsed.ModelName == "Lodgy");
        Assert.Equal(2012, lodgy.Parsed.BuildYearFrom);
        Assert.Null(lodgy.Parsed.BuildYearTo);

        var bigsterHybrid = Assert.Single(entries, e => e.Parsed.Variant == "Bigster Hybrid");
        Assert.Equal("SUV", bigsterHybrid.Parsed.BodyType);
        Assert.Equal("Hybrid", bigsterHybrid.Parsed.FuelType);
        Assert.Equal("Dacia_Bigster_Hybrid_SUV_2025_5d_Hybrid_(Electric)_DE.pdf", bigsterHybrid.RawFileNameOrLabel.Split('/')[^1]);
    }

    [Fact]
    public void Constructor_RejectsBrandsOutsideRenaultGroup() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RenaultGroupRescueCardSource(Brand.Toyota, new StubHttpClientFactory(), NullLogger<RenaultGroupRescueCardSource>.Instance));
}
