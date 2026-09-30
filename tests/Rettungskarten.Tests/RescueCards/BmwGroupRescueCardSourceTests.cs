using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.BmwGroup;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixtures (bmwgroup_rescue_sheets_*.json) are trimmed real responses of the AOS rescue-sheet API
/// (aos.bmwgroup.com, 2026-09), picked for the tricky entries: a key with a space and an "é", i-models
/// filed under their combustion sibling's series (i4 under "4-series", iX3 NA5 under "iX"), BMW's
/// glued "ab 112019" date and "ab - 11/2018", an English label in the German list, MINI's F65 filed
/// under "clubman", the "MINI Coupé E", and Rolls-Royce's Cullinan filed under "sedan".
/// </summary>
public sealed class BmwGroupRescueCardSourceTests : IDisposable
{
    public BmwGroupRescueCardSourceTests() => Strings.OverrideCulture = CultureInfo.GetCultureInfo("en");

    public void Dispose() => Strings.OverrideCulture = null;

    private static async Task<IReadOnlyList<RescueCardEntry>> DiscoverFromFixture(Brand brand, string apiBrand, string fixture)
    {
        var json = await File.ReadAllTextAsync(Path.Combine("Fixtures", fixture));
        var factory = new StubHttpClientFactory().Json(BmwGroupRescueCardSource.BuildListUrl(apiBrand, 0), json);
        var source = new BmwGroupRescueCardSource(brand, factory, NullLogger<BmwGroupRescueCardSource>.Instance);
        return await source.DiscoverAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DiscoverAsync_Bmw_ParsesSeriesChassisCodeYearsAndDrivetrain()
    {
        var entries = await DiscoverFromFixture(Brand.BMW, "bmw", "bmwgroup_rescue_sheets_bmw.json");

        Assert.Equal(13, entries.Count);
        Assert.All(entries, e => Assert.Equal("DE", e.Parsed.LanguageCode));
        Assert.Equal(entries.Count, entries.Select(e => e.RawFileNameOrLabel).Distinct().Count());

        var f45 = Assert.Single(entries, e => e.Parsed.ChassisCode == "F45");
        Assert.Equal("2er", f45.Parsed.ModelName);
        Assert.Equal("Van", f45.Parsed.BodyType);
        Assert.Equal(2014, f45.Parsed.BuildYearFrom);
        Assert.Null(f45.Parsed.BuildYearTo);
        Assert.Equal("Rescue-information/BMW/compact-van/2-series/de_2er-Reihe-F45.pdf", f45.RawFileNameOrLabel);

        var i4 = Assert.Single(entries, e => e.Parsed.ChassisCode == "G26");
        Assert.Equal("i4", i4.Parsed.ModelName);
        Assert.Equal("BEV", i4.Parsed.FuelType);
        Assert.Equal("Gran Coupé", i4.Parsed.BodyType);

        var ix3 = Assert.Single(entries, e => e.Parsed.ChassisCode == "NA5");
        Assert.Equal("iX3", ix3.Parsed.ModelName); // filed under series "iX", but the label names the iX3
        Assert.Equal(5, ix3.Parsed.Doors);

        var ix = Assert.Single(entries, e => e.Parsed.ChassisCode == "I20");
        Assert.Equal("iX", ix.Parsed.ModelName);

        var z4 = Assert.Single(entries, e => e.Parsed.ChassisCode == "G29");
        Assert.Equal("Z4", z4.Parsed.ModelName);
        Assert.Equal("Roadster", z4.Parsed.BodyType);
        Assert.Equal(2018, z4.Parsed.BuildYearFrom); // "(ab - 11/2018)"

        var x3Phev = Assert.Single(entries, e => e.Parsed.ChassisCode == "G01");
        Assert.Equal(2019, x3Phev.Parsed.BuildYearFrom); // "(ab 112019)"
        Assert.Equal("PHEV", x3Phev.Parsed.FuelType);

        var x5 = Assert.Single(entries, e => e.Parsed.ChassisCode == "F15");
        Assert.Equal("PHEV", x5.Parsed.FuelType); // "X5 ActiveHybrid F15PHEV" - PHEV beats Hybrid

        var m3 = Assert.Single(entries, e => e.Parsed.ChassisCode == "G80");
        Assert.Equal("3er", m3.Parsed.ModelName);

        var i5Touring = Assert.Single(entries, e => e.Parsed.ChassisCode == "G61");
        Assert.Equal("i5", i5Touring.Parsed.ModelName); // English label "5-Series I5 G61 BEV (since 03/2024)"
        Assert.Equal("Touring", i5Touring.Parsed.BodyType);
        Assert.Equal(2024, i5Touring.Parsed.BuildYearFrom);

        var active7 = Assert.Single(entries, e => e.Parsed.ChassisCode == "F01/F02/F04");
        Assert.Equal("Hybrid", active7.Parsed.FuelType);

        var compact = Assert.Single(entries, e => e.Parsed.ChassisCode == "E36");
        Assert.Equal("Compact", compact.Parsed.BodyType);
        Assert.Equal(1994, compact.Parsed.BuildYearFrom);
        Assert.Equal(2000, compact.Parsed.BuildYearTo);

        var i3 = Assert.Single(entries, e => e.Parsed.ChassisCode == "I01");
        Assert.Equal("i3", i3.Parsed.ModelName);
        Assert.Equal("Schrägheck", i3.Parsed.BodyType);
        Assert.Equal(2013, i3.Parsed.BuildYearFrom);
        Assert.Equal(2015, i3.Parsed.BuildYearTo);
    }

    [Fact]
    public async Task DiscoverAsync_Bmw_DownloadUrlIsTheStableEndpointWithEscapedKey()
    {
        var entries = await DiscoverFromFixture(Brand.BMW, "bmw", "bmwgroup_rescue_sheets_bmw.json");

        var f74 = Assert.Single(entries, e => e.Parsed.ChassisCode == "F74");
        Assert.Equal(
            "https://aos.bmwgroup.com/api/v2/downloads?key=Rescue-information%2FBMW%2Fcoupe-compact%2F2-series%2FVUL-BMW_2er%20Series_F74_Coup%C3%A9_2024_5d_GD_de-DE.pdf&signed=true",
            f74.DownloadUrl);
        Assert.DoesNotContain("X-Amz", f74.DownloadUrl);
        Assert.Equal("Gran Coupé", f74.Parsed.BodyType);
        Assert.Equal("ICE", f74.Parsed.FuelType); // "_GD_" in the filename
    }

    [Fact]
    public async Task DiscoverAsync_Mini_CorrectsMisfiledBodyTypesAndNamesModels()
    {
        var entries = await DiscoverFromFixture(Brand.Mini, "mini", "bmwgroup_rescue_sheets_mini.json");

        Assert.Equal(7, entries.Count);

        var f65 = Assert.Single(entries, e => e.Parsed.ChassisCode == "F65");
        Assert.Equal("Cooper", f65.Parsed.ModelName);
        Assert.Equal("Schrägheck", f65.Parsed.BodyType); // the portal files it under "clubman"
        Assert.Equal(5, f65.Parsed.Doors);

        var clubman = Assert.Single(entries, e => e.Parsed.ChassisCode == "F54");
        Assert.Equal("Clubman", clubman.Parsed.ModelName);
        Assert.Equal("Kombi", clubman.Parsed.BodyType);

        var miniE = Assert.Single(entries, e => e.Parsed.Variant == "MINI Coupé E");
        Assert.Equal("MINI E", miniE.Parsed.ModelName);
        Assert.Equal("BEV", miniE.Parsed.FuelType);

        var r53 = Assert.Single(entries, e => e.Parsed.ChassisCode == "R53");
        Assert.Equal("MINI", r53.Parsed.ModelName);
        Assert.Equal("MINI R53 (Cooper S)", r53.Parsed.Variant);
        Assert.Equal(2001, r53.Parsed.BuildYearFrom);
        Assert.Equal(2006, r53.Parsed.BuildYearTo);

        var f56Electric = Assert.Single(entries, e => e.Parsed.ChassisCode == "F56");
        Assert.Equal("BEV", f56Electric.Parsed.FuelType);
        Assert.Null(f56Electric.Parsed.BuildYearFrom); // the label states no date at all

        var roadster = Assert.Single(entries, e => e.Parsed.ChassisCode == "R59");
        Assert.Equal("Roadster", roadster.Parsed.BodyType);
    }

    [Fact]
    public async Task DiscoverAsync_RollsRoyce_NormalizesChassisCodeAndCorrectsCategory()
    {
        var entries = await DiscoverFromFixture(Brand.RollsRoyce, "rolls-royce", "bmwgroup_rescue_sheets_rollsroyce.json");

        Assert.Equal(4, entries.Count);

        var cullinan = Assert.Single(entries, e => e.Parsed.ModelName == "Cullinan");
        Assert.Equal("RR31", cullinan.Parsed.ChassisCode);
        Assert.Equal("SUV", cullinan.Parsed.BodyType); // filed under "sedan"

        var dawn = Assert.Single(entries, e => e.Parsed.ModelName == "Dawn");
        Assert.Equal("RR6", dawn.Parsed.ChassisCode); // "RR06"
        Assert.Equal("Cabriolet", dawn.Parsed.BodyType); // filed under "coupe"

        var spectre = Assert.Single(entries, e => e.Parsed.ModelName == "Spectre");
        Assert.Equal("Coupé", spectre.Parsed.BodyType);
        Assert.Equal("BEV", spectre.Parsed.FuelType);

        var phantomDrophead = Assert.Single(entries, e => e.Parsed.ChassisCode == "RR2");
        Assert.Equal("Phantom", phantomDrophead.Parsed.ModelName);
        Assert.Equal("Cabriolet", phantomDrophead.Parsed.BodyType);
    }

    [Fact]
    public async Task DiscoverAsync_SkipsIncompleteForeignAndDuplicateEntries_AndPages()
    {
        var fixture = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine("Fixtures", "bmwgroup_rescue_sheets_mini.json")))!;
        var rows = fixture["data"]!.AsArray().Select(r => r!.DeepClone()).ToList();

        var foreignBrand = rows[0].DeepClone();
        foreignBrand["brand"] = "bmw";
        foreignBrand["key"] = "Rescue-information/BMW/other.pdf";
        var foreignLanguage = rows[1].DeepClone();
        foreignLanguage["language"] = "en-GB";
        foreignLanguage["key"] = "Rescue-information/mini/other_en-GB.pdf";
        var noKey = rows[2].DeepClone();
        noKey["key"] = null;
        var duplicate = rows[3].DeepClone();

        // Page 1: the 7 real rows; page 2 (offset 7): the 4 bad rows. meta.count spans both pages.
        var page1 = new JsonObject { ["meta"] = new JsonObject { ["count"] = 11 }, ["data"] = new JsonArray([.. rows]) };
        var page2 = new JsonObject
        {
            ["meta"] = new JsonObject { ["count"] = 11 },
            ["data"] = new JsonArray(foreignBrand, foreignLanguage, noKey, duplicate)
        };

        var factory = new StubHttpClientFactory()
            .Json(BmwGroupRescueCardSource.BuildListUrl("mini", 0), page1.ToJsonString())
            .Json(BmwGroupRescueCardSource.BuildListUrl("mini", 7), page2.ToJsonString());
        var source = new BmwGroupRescueCardSource(Brand.Mini, factory, NullLogger<BmwGroupRescueCardSource>.Instance);

        var entries = await source.DiscoverAsync(CancellationToken.None);

        Assert.Equal(7, entries.Count);
        Assert.Equal(2, factory.Requests.Count);
        Assert.All(entries, e => Assert.Equal(Brand.Mini, e.Brand));
    }

    [Fact]
    public async Task DiscoverAsync_ListRequestFails_Throws()
    {
        var factory = new StubHttpClientFactory().Status(BmwGroupRescueCardSource.BuildListUrl("bmw", 0), HttpStatusCode.InternalServerError);
        var source = new BmwGroupRescueCardSource(Brand.BMW, factory, NullLogger<BmwGroupRescueCardSource>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.DiscoverAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_FetchesTheStableDownloadEndpoint()
    {
        var key = "Rescue-information/mini/coupe/f56/de_MINI-F56.pdf";
        var downloadUrl = BmwGroupRescueCardSource.BuildDownloadUrl(key);
        var factory = new StubHttpClientFactory().Bytes(downloadUrl, StubHttpClientFactory.FakePdf());
        var source = new BmwGroupRescueCardSource(Brand.Mini, factory, NullLogger<BmwGroupRescueCardSource>.Instance);
        var entry = new RescueCardEntry(Brand.Mini, "https://aos.bmwgroup.com/", downloadUrl, key,
            new ParsedModelInfo("MINI", null, null, null, null, null, null, "DE", ParseConfidence.High));

        var result = await source.DownloadAsync(entry, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public void Constructor_RejectsBrandsOutsideBmwGroup() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BmwGroupRescueCardSource(Brand.VW, new StubHttpClientFactory(), NullLogger<BmwGroupRescueCardSource>.Instance));
}
