using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Infrastructure.RescueCards.Parsing;
using Rettungskarten.Infrastructure.RescueCards.Splitting;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixtures are trimmed real responses of fordserviceinfo.com: the lookup page (year list cut to 2025,
/// 2021 and 2018), the vehicle lists for 2025 and 2021 (cut to a few lines, incl. other-market and
/// North-America-only ones that must not be looked up) and three lookup results - Puma 2025 (its card
/// plus the combined PDF), Transit Custom 2025 (three cards) and Mustang Mach-E 2021 (a card plus an
/// ERG). ford_sample_pages.pdf is a 10-page slice of the combined PDF (images replaced by 1x1
/// placeholders): a contents page, B-MAX, Grand C-MAX, the C-MAX Energi card with four header-less
/// high-voltage pages, Focus Turnier and Transit Courier (header without dates).
///
/// No test here asserts on localized text (only on parsed data), so none sets
/// <c>Strings.OverrideCulture</c>: that process-global switch would only race with the culture tests
/// running in parallel (RunSummaryPrinterTests, StringsTests).
/// </summary>
public sealed class FordRescueCardSourceTests
{
    private const string ModelsUrl = FordRescueCardSource.PortalBaseUrl + "/ServiceTip/GetModels?year=";

    private static string Fixture(string name) => File.ReadAllText(Path.Combine("Fixtures", name));

    private static StubHttpClientFactory CreatePortal()
    {
        var lookups = new Dictionary<string, string>
        {
            ["7239"] = Fixture("ford_lookup_puma_2025.html"),
            ["7261"] = Fixture("ford_lookup_transit_custom_2025.html"),
            ["6790"] = Fixture("ford_lookup_mache_2021.html")
        };

        return new StubHttpClientFactory()
            .Html(FordRescueCardSource.RescuePageUrl, Fixture("ford_rescue_page.html"))
            .Json(ModelsUrl + "2025", Fixture("ford_models_2025.json"))
            .Json(ModelsUrl + "2021", Fixture("ford_models_2021.json"))
            .Respond(FordRescueCardSource.LookupUrl, request =>
            {
                var form = request.Content!.ReadAsStringAsync().Result;
                var vehicleId = form.Split('&').Single(p => p.StartsWith("VehicleId=")).Split('=')[1];
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    // An unexpected lookup (a filtered-out vehicle line) fails the test here.
                    Content = new StringContent(lookups[vehicleId], System.Text.Encoding.UTF8, "text/html")
                };
            });
    }

    [Fact]
    public async Task DiscoverAsync_CollectsCardsOfEveryEuropeanLookup()
    {
        var factory = CreatePortal();

        var entries = await new FordRescueCardSource(factory, NullLogger<FordRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        // Puma, 3x Transit Custom, Mach-E, the combined PDF; the Mach-E ERG is dropped. Only 2019+
        // model years are queried, and neither "Escape (CX482 NA)", "F-150" nor "Ranger (P703M S.Africa)".
        Assert.Equal(6, entries.Count);
        Assert.DoesNotContain(factory.Requests, r => r.Request.RequestUri!.Query.Contains("2018"));
        Assert.Equal(3, factory.Requests.Count(r => r.Request.Method == HttpMethod.Post));
        Assert.All(factory.Requests, r => Assert.Contains("UserCountry=", r.Request.Headers.GetValues("Cookie").Single()));
    }

    [Fact]
    public async Task DiscoverAsync_ParsesCardsAndMarksCombinedPdf()
    {
        var entries = await new FordRescueCardSource(CreatePortal(), NullLogger<FordRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);

        var combined = Assert.Single(entries, e => e.Scope == DocumentScope.Combined);
        Assert.Equal("https://www.fordservicecontent.com/Ford_Content/Catalog/service_tip_files/EU-rescue-cards-all-carlines_deDEU.pdf", combined.DownloadUrl);

        var transitCustom = entries.Where(e => e.Parsed.ModelName == "Transit Custom").ToList();
        Assert.Equal(3, transitCustom.Count);
        Assert.All(transitCustom, e => Assert.Equal("V710E", e.Parsed.ChassisCode));

        // "Mustang-MachE-de.pdf" follows no convention: model from the vehicle line, year from the label.
        var machE = Assert.Single(entries, e => e.Parsed.ModelName == "Mustang Mach-E");
        Assert.Equal(2021, machE.Parsed.BuildYearFrom);
        Assert.Equal("DE", machE.Parsed.LanguageCode);
    }

    [Fact]
    public async Task DiscoverAsync_Throws_WhenPortalRedirectsToCountrySelection()
    {
        var factory = new StubHttpClientFactory().Respond(FordRescueCardSource.RescuePageUrl, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html></html>"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, FordRescueCardSource.PortalBaseUrl + "/SetCountry?returnUrl=%2FServiceTip")
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FordRescueCardSource(factory, NullLogger<FordRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("Puma", true)]
    [InlineData("Transit Custom - TU (V710E)", true)]
    [InlineData("Kuga - TD (CX482 EU)", true)]
    [InlineData("Ranger - RB (P703M  S.Africa)", false)]
    [InlineData("Escape - TC (CX482 NA)", false)]
    [InlineData("F-150", false)]
    public void IsEuropeanVehicleLine(string model, bool expected) =>
        Assert.Equal(expected, FordRescueCardSource.IsEuropeanVehicleLine(model));

    [Theory]
    [InlineData("Ford_Transit_Custom_Van_2025_5d_Electric_DE.pdf", "Transit Custom", 2025)]
    [InlineData("G2210196-deDEU-2.0.pdf", "Kuga", 2020)]
    public void ParsePortalCard_JoinsTwoWordModelsAndFallsBackToVehicleLine(string fileName, string model, int year)
    {
        var parsed = FordRescueCardParser.ParsePortalCard(fileName, "2020 Kuga FHEV Rettungskarte", "Kuga - TD (CX482 EU)", 2020);

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(year, parsed.BuildYearFrom);
        Assert.Equal("DE", parsed.LanguageCode);
    }

    [Theory]
    [InlineData("Focus - LPGTurnier08/2007 – 03/2011 F", "Focus", "Turnier", "LPG", 2007, 2011)]
    [InlineData("C-MAXGrand MAV (5+2-Sitzer)03/2015 – > C", "Grand C-MAX", "MAV", null, 2015, null)]
    [InlineData("Fiesta3-Türer06/2008 – 04/2017 F", "Fiesta", null, null, 2008, 2017)]
    [InlineData("Ka+05/2016 – > K", "Ka+", null, null, 2016, null)]
    [InlineData("Transit 2014.5Kastenwagen08/2013 – > T", "Transit", "Kastenwagen", null, 2013, null)]
    public void ParseCombinedHeader_SplitsGluedHeaderText(string header, string model, string? body, string? fuel, int from, int? to)
    {
        var parsed = FordRescueCardParser.ParseCombinedHeader(header);

        Assert.Equal(model, parsed.ModelName);
        Assert.Equal(body, parsed.BodyType);
        Assert.Equal(fuel, parsed.FuelType);
        Assert.Equal(from, parsed.BuildYearFrom);
        Assert.Equal(to, parsed.BuildYearTo);
    }

    [Fact]
    public void Layout_GroupsCardsWithTheirHighVoltagePages_FromRealSample()
    {
        var results = CombinedPdfSplitter.Split(File.ReadAllBytes(Path.Combine("Fixtures", "ford_sample_pages.pdf")), new FordCombinedPdfLayout());

        Assert.Equal(["B-MAX", "Grand C-MAX", "C-MAX", "Focus", "Transit Courier"], results.Select(r => r.Parsed.ModelName));

        var energi = results[2];
        Assert.Equal("HEV", energi.Parsed.FuelType);
        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(energi.PdfBytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(5, pdf.PageCount);

        Assert.Equal("Turnier", results[3].Parsed.BodyType);
        Assert.Null(results[4].Parsed.BuildYearFrom); // "Transit CourierT": the header states no dates
        Assert.Equal(results.Count, results.Select(r => r.DocumentId).Distinct().Count());
    }
}
