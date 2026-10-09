using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.BmwGroup;

/// <summary>
/// BMW, MINI and Rolls-Royce publish their rescue sheets through one shared portal, BMW Group's
/// "Aftersales Online System" (aos.bmwgroup.com, an Angular SPA - its robots.txt URL just returns the
/// SPA shell, i.e. no rules). The SPA reads a public JSON API that needs no login:
/// <c>GET /api/v2/rescue-sheets?language=de-DE&amp;client=ROW&amp;brand={bmw|mini|rolls-royce}&amp;limit=..&amp;offset=..</c>
/// answers <c>{"meta":{"count":..},"data":[{bodyType, series, key, language, name}, ...]}</c>, one entry
/// per sheet (verified 2026-09: 170 BMW, 26 MINI, 13 Rolls-Royce sheets in German). Findings that
/// shaped this class:
///
/// - The <c>language</c> filter must be a full locale ("de-DE"); without it the API returns every
///   language (~5,000 entries). Comparing the de-DE list with en-GB showed no model that only has an
///   English sheet (the en-GB list's extra entries are older duplicates of models whose German sheet
///   exists), so only German is requested - no English fallback is needed for these brands. "ROW" (rest
///   of world) is the client the SPA itself uses; it also keeps the US-market sheets out.
/// - The PDF is fetched via <c>/api/v2/downloads?key={key}&amp;signed=true</c>, which answers 307 with
///   a pre-signed S3 URL valid for 2 hours. That signed URL must never be persisted - so the entry's
///   <c>DownloadUrl</c> is the stable downloads endpoint itself and the redirect is resolved at download
///   time. No <c>ResolveDownloadUrlAsync</c> override is needed for that: the HTTP pipeline's primary
///   handler follows the 307 on its own (verified live).
/// - The <c>key</c> (S3 object path, e.g. "Rescue-information/BMW/sedan/3-series/de_3er-Reihe-E90.pdf")
///   is unique and stable, so it is the entry's <see cref="RescueCardEntry.RawFileNameOrLabel"/>. Keys
///   contain spaces and non-ASCII characters ("..._F74_Coupé_2024_5d_GD_de-DE.pdf") and must be
///   percent-escaped as a whole query value.
/// - Metadata comes from the entry's three free-ish fields, each with its own quirks (the portal files
///   several models under the wrong body category) - see <see cref="BmwGroupRescueSheetParser"/>.
///
/// One class serves all three brands; each brand is its own instance (and its own API request).
/// </summary>
public sealed class BmwGroupRescueCardSource(
    Brand brand, IHttpClientFactory httpClientFactory, ILogger<BmwGroupRescueCardSource> logger) : RescueCardSourceBase(httpClientFactory)
{
    private const string ApiBaseUrl = "https://aos.bmwgroup.com/api/v2/";
    private const int PageSize = 1000;

    // Safety net against a misbehaving API (e.g. ignoring offset and repeating page 1 forever).
    private const int MaxPages = 20;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public override Brand Brand { get; } = brand is Brand.BMW or Brand.Mini or Brand.RollsRoyce
        ? brand
        : throw new ArgumentOutOfRangeException(nameof(brand), brand, "Not a BMW Group brand served by aos.bmwgroup.com.");

    // Most sheets are 1-2MB, but the newer high-voltage ones reach ~20MB (i7 G70: 19.9MB, X2 U10:
    // 11.9MB) - too close to the default client's 60s budget on a slow connection.
    protected override string DownloadClientName => RettungskartenHttpClient.LargeDownloadName;

    private string ApiBrand => Brand switch
    {
        Brand.BMW => "bmw",
        Brand.Mini => "mini",
        _ => "rolls-royce",
    };

    public static string BuildListUrl(string apiBrand, int offset) =>
        $"{ApiBaseUrl}rescue-sheets?language=de-DE&client=ROW&brand={apiBrand}&limit={PageSize}&offset={offset}";

    public static string BuildDownloadUrl(string key) =>
        $"{ApiBaseUrl}downloads?key={Uri.EscapeDataString(key)}&signed=true";

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();
        var sheets = new List<SheetDto>();
        var firstPageUrl = BuildListUrl(ApiBrand, 0);

        for (var page = 0; page < MaxPages; page++)
        {
            // A failing list request propagates on purpose - that's the link-check signal.
            var json = await client.GetStringAsync(BuildListUrl(ApiBrand, sheets.Count), ct);
            var response = JsonSerializer.Deserialize<SheetListDto>(json, JsonOptions);
            var data = response?.Data ?? [];
            sheets.AddRange(data);

            if (data.Count == 0 || response?.Meta?.Count is not { } total || sheets.Count >= total)
            {
                break;
            }
        }

        var entries = new List<RescueCardEntry>(sheets.Count);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sheet in sheets)
        {
            if (string.IsNullOrWhiteSpace(sheet.Key) || string.IsNullOrWhiteSpace(sheet.Name))
            {
                logger.LogWarning("{Message}", Strings.Get("RescueCards_BmwGroup_IncompleteEntry", Brand, sheet.Id ?? "?"));
                continue;
            }

            // The brand/language filters are the API's job, but a sheet of another brand or language
            // slipping through would be persisted under the wrong brand or as a "German" sheet.
            if (!string.Equals(sheet.Brand, ApiBrand, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(sheet.Language, "de-DE", StringComparison.OrdinalIgnoreCase)
                || !RescueDocumentClassifier.IsRescueSheet(sheet.Name, sheet.Key)
                || !seenKeys.Add(sheet.Key))
            {
                continue;
            }

            var parsed = BmwGroupRescueSheetParser.Parse(Brand, sheet.Name.Trim(), sheet.BodyType?.Name, sheet.Series?.Name, sheet.Key);
            entries.Add(new RescueCardEntry(Brand, firstPageUrl, BuildDownloadUrl(sheet.Key), sheet.Key, parsed));
        }

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, entries.Count));
        return entries;
    }

    private sealed record SheetListDto(MetaDto? Meta, List<SheetDto>? Data);

    private sealed record MetaDto(int? Count);

    private sealed record SheetDto(string? Id, string? Brand, NamedDto? BodyType, NamedDto? Series, string? Key, string? Language, string? Name);

    private sealed record NamedDto(string? Name);
}
