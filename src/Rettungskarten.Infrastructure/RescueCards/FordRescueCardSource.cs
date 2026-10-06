using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Ford's rescue cards come from Ford's own service portal: ford.de's help article "Wo finde ich die
/// Rettungsdatenblätter?" points to <c>fordserviceinfo.com/ServiceTip</c>, whose "Referenzhandbücher
/// (einschließlich Rettungskarten)" category is a vehicle lookup - pick a model year, then a vehicle
/// line, submit - that lists the cards for that exact year and line as links to
/// <c>fordservicecontent.com/Ford_Content/Catalog/service_tip_files/...pdf</c>. Every lookup also
/// lists "EU-Rescue-Cards-All-Carlines_deDEU", Ford's combined 204-page PDF with every European model
/// up to ~2020.
///
/// Verified against the live portal (2026-09), and why the code does what it does:
/// - no headless browser is needed: the portal is plain ASP.NET MVC. The market is chosen once on a
///   "SetCountry" form, which does nothing but set a <c>UserCountry</c> cookie; sending that cookie
///   (<see cref="GermanMarketCookie"/>, exactly the value the form sets for Germany/German) makes every
///   page answer for the German market. Without it every request redirects to the form - checked
///   explicitly, so a change on Ford's side fails discovery (the link-check signal) instead of
///   silently returning nothing. The lookup POST doesn't validate the page's anti-forgery token.
/// - the vehicle lists (<c>/ServiceTip/GetModels?year=</c>, JSON) go back to 1982, but lookups for
///   model years before 2019 only ever return the combined PDF (sampled across Edge, Mustang, Transit
///   Custom, Kuga and Focus Electric, 2016-2018). Querying only <see cref="FirstPortalModelYear"/> and
///   later keeps discovery at ~210 requests (~4-5 minutes at the per-host politeness delay) instead of
///   ~1,900 without losing a card.
/// - cards are attached per model year, not per vehicle line (the 2021 and 2022 Mustang Mach-E list
///   its card, 2024/2025 don't), so every (year, vehicle line) pair is looked up; a card listed under
///   several is kept once, with the metadata of its earliest listing.
/// - the vehicle lists also contain other markets' lines ("Ranger - KD (P375 Thailand)", "Escape - TC
///   (CX482 NA)") and North-America-only nameplates (F-150, Expedition, Lincoln models) - skipped,
///   they never carry German cards.
/// - fordservicecontent.com is behind Akamai and rejects non-browser requests with 403, so downloads
///   use the browser-header client; fordserviceinfo.com itself isn't.
/// ERGs listed next to the cards ("2021 Mach-E Emergency Response Guide") are dropped by
/// <see cref="RescueDocumentClassifier"/>; the combined PDF is one <see cref="DocumentScope.Combined"/>
/// entry, split per model by <c>split ford</c> (<c>FordCombinedPdfLayout</c>).
/// </summary>
public sealed class FordRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<FordRescueCardSource> logger)
    : RescueCardSourceBase(httpClientFactory)
{
    public const string PortalBaseUrl = "https://www.fordserviceinfo.com";
    public const string RescuePageUrl = PortalBaseUrl + "/ServiceTip?category=RESCUE";
    public const string LookupUrl = PortalBaseUrl + "/ServiceTip/Get";

    public const string CombinedModelName = "All Models";

    /// <summary>The <c>UserCountry</c> cookie the portal's SetCountry form sets for Germany (market 67)
    /// and German.</summary>
    public const string GermanMarketCookie =
        "UserCountry=marketID%3D67%26language%3DDE%26languageFMA%3Dde_de%26langIETF%3Dde-DE%26country%3DDEU%26ParentCode%3DEU%20%26IsSelected%3DYES";

    /// <summary>Earliest model year that lists individual cards (see the class comment).</summary>
    public const int FirstPortalModelYear = 2019;

    private const string DefaultCategoryId = "45";

    // Only an explicit market name marks another market: European lines' parentheses often hold just
    // the platform code ("Transit Custom - TU (V710E)", "Tourneo Courier - HQ (V769)").
    private static readonly Regex OtherMarketLine = new(
        @"\([^)]*\b(?:NA|SA|AP|China|India|Brazil|Vietnam|Thailand|Russia|S\.Africa|South Africa|Argentina|Taiwan|Australia|Mexico)\)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex NorthAmericaOnly = new(
        @"^(?:Aviator|Continental|Corsair|E-\d+|Expedition|F-\d+|Flex|MK[A-Z]|Nautilus|Navigator|Police|Taurus|Escape|Fusion)\b",
        RegexOptions.Compiled);

    private static readonly Regex CombinedFileName = new(@"all-carlines", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override Brand Brand => Brand.Ford;

    protected override string DownloadClientName => RettungskartenHttpClient.BrowserName;

    public sealed record PortalVehicle(int Id, int Year, string Model);

    private sealed record FoundCard(string Url, string Label, PortalVehicle Vehicle);

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();

        // The lookup page: proves the market cookie is accepted and lists the selectable model years.
        var (page, finalUrl) = await GetAsync(client, RescuePageUrl, ct);
        EnsureMarketAccepted(finalUrl);

        var document = await BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(page), ct);
        var categoryId = document.QuerySelector("input#categoryID")?.GetAttribute("value") ?? DefaultCategoryId;
        var years = document.QuerySelectorAll("select#Year option")
            .Select(o => int.TryParse(o.TextContent.Trim(), out var y) ? y : 0)
            .Where(y => y >= FirstPortalModelYear)
            .OrderBy(y => y)
            .ToList();

        var vehicles = new List<PortalVehicle>();
        foreach (var year in years)
        {
            vehicles.AddRange(await GetVehiclesAsync(client, year, ct));
        }

        var relevant = vehicles.Where(v => IsEuropeanVehicleLine(v.Model)).ToList();
        logger.LogInformation("{Message}", Strings.Get("RescueCards_Ford_PortalLookups", relevant.Count));

        var cards = new Dictionary<string, FoundCard>(StringComparer.OrdinalIgnoreCase);
        foreach (var vehicle in relevant)
        {
            foreach (var (url, label) in await LookupAsync(client, vehicle, categoryId, ct))
            {
                cards.TryAdd(url, new FoundCard(url, label, vehicle));
            }
        }

        var entries = cards.Values.Select(ToEntry).Where(e => e is not null).Select(e => e!).ToList();
        var preferred = LanguagePreference.PreferGermanThenEnglish(
            entries,
            // Labels differ between a card's language versions, so the group is model + year + fuel.
            e => $"{e.Parsed.ModelName}|{e.Parsed.BuildYearFrom}|{e.Parsed.FuelType}",
            e => e.Parsed.LanguageCode);

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, preferred.Count));
        return preferred;
    }

    /// <summary>Other markets' lines carry their market in parentheses ("(P375 Thailand)", "(CX482
    /// NA)"); European ones say "EU" there or have no marker at all ("Puma", "Tourneo Connect - FJ").</summary>
    public static bool IsEuropeanVehicleLine(string model) =>
        !OtherMarketLine.IsMatch(model) && !NorthAmericaOnly.IsMatch(model);

    private RescueCardEntry? ToEntry(FoundCard card)
    {
        var fileName = HttpDownloadHelper.GetFileName(card.Url);
        if (!RescueDocumentClassifier.IsRescueSheet(card.Label, fileName))
        {
            return null;
        }

        if (CombinedFileName.IsMatch(fileName))
        {
            var combined = new ParsedModelInfo(CombinedModelName, card.Label, null, null, null, null, null, "DE", ParseConfidence.Heuristic);
            return new RescueCardEntry(Brand, RescuePageUrl, card.Url, fileName, combined, DocumentScope.Combined);
        }

        var parsed = FordRescueCardParser.ParsePortalCard(fileName, card.Label, card.Vehicle.Model, card.Vehicle.Year);
        return new RescueCardEntry(Brand, RescuePageUrl, card.Url, fileName, parsed);
    }

    private async Task<IReadOnlyList<PortalVehicle>> GetVehiclesAsync(HttpClient client, int year, CancellationToken ct)
    {
        var url = $"{PortalBaseUrl}/ServiceTip/GetModels?year={year}";
        try
        {
            var (json, finalUrl) = await GetAsync(client, url, ct);
            EnsureMarketAccepted(finalUrl);
            return ParseVehicles(json);
        }
        catch (Exception ex) when (ex is JsonException || HttpRequestFailures.IsRequestFailure(ex, ct))
        {
            logger.LogWarning(ex, "{Message}", Strings.Get("RescueCards_Generic_PageReadFailed", Brand, url));
            return [];
        }
    }

    public static IReadOnlyList<PortalVehicle> ParseVehicles(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray()
            .Select(e => new PortalVehicle(e.GetProperty("Id").GetInt32(), e.GetProperty("Year").GetInt32(), e.GetProperty("Model").GetString() ?? string.Empty))
            .Where(v => v.Model.Length > 0)
            .ToList();
    }

    private async Task<IReadOnlyList<(string Url, string Label)>> LookupAsync(
        HttpClient client, PortalVehicle vehicle, string categoryId, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, LookupUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["categoryID"] = categoryId,
                    ["Year"] = vehicle.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["VehicleId"] = vehicle.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["vin"] = string.Empty
                })
            };
            request.Headers.TryAddWithoutValidation("Cookie", GermanMarketCookie);
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            EnsureMarketAccepted(response.RequestMessage?.RequestUri);
            return ParseLookupResult(await response.Content.ReadAsStringAsync(ct), LookupUrl);
        }
        catch (Exception ex) when (HttpRequestFailures.IsRequestFailure(ex, ct))
        {
            logger.LogWarning(ex, "{Message}", Strings.Get("RescueCards_Generic_PageReadFailed", Brand, $"{LookupUrl} ({vehicle.Model} {vehicle.Year})"));
            return [];
        }
    }

    /// <summary>The card links of one lookup result page (label = link text).</summary>
    public static IReadOnlyList<(string Url, string Label)> ParseLookupResult(string html, string pageUrl)
    {
        var results = new List<(string, string)>();
        foreach (Match m in Regex.Matches(html, @"<a\s[^>]*href=""(?<href>[^""]+\.pdf)""[^>]*>(?<text>[^<]*)</a>", RegexOptions.IgnoreCase))
        {
            string url;
            try
            {
                url = HttpDownloadHelper.ResolveUrl(pageUrl, System.Net.WebUtility.HtmlDecode(m.Groups["href"].Value));
            }
            catch (UriFormatException)
            {
                continue;
            }

            results.Add((url, System.Net.WebUtility.HtmlDecode(m.Groups["text"].Value).Trim()));
        }

        return results;
    }

    private static async Task<(string Body, Uri? FinalUrl)> GetAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cookie", GermanMarketCookie);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync(ct), response.RequestMessage?.RequestUri);
    }

    /// <summary>Redirected to the country form: the market cookie is no longer accepted. Thrown as
    /// HttpRequestException so it fails discovery like any other unreachable overview page.</summary>
    private static void EnsureMarketAccepted(Uri? finalUrl)
    {
        if (finalUrl is not null && finalUrl.AbsolutePath.StartsWith("/SetCountry", StringComparison.OrdinalIgnoreCase))
        {
            // Not an HttpRequestException: the per-year and per-vehicle lookups catch those to skip one
            // failed request, but a rejected market cookie means every further lookup is wrong too, so it
            // must fail discovery instead of silently shrinking the result.
            throw new InvalidOperationException(Strings.Get("RescueCards_Ford_MarketNotAccepted", finalUrl));
        }
    }
}
