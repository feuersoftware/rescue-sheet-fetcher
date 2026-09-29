using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Matching;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.IfzBerlin;

/// <summary>
/// Opel (and Vauxhall), Saab, Chevrolet and Cadillac rescue sheets are published by IFZ Berlin, the
/// service provider behind the "Stellantis Aftersales" rescue portal that opel.de links to - one
/// instance of this source per brand. The portal (www.ifz-berlin.de, an AngularJS app) loads
/// everything from a small JSON API behind <c>ifz-berlin.de/proxy/</c>, which answers 403 unless the
/// request carries the portal's own page as Referer (verified; the PDFs themselves don't need it):
///
/// - <c>index_ret_8_3.php?fabrikat_nr=N</c>: the model groups ("auto_typ") of one make -
///   1 = Opel/Vauxhall, 2 = Chevrolet, 3 = Cadillac, 99 = Saab;
/// - <c>index_ret_8_4.php?land_id=L&amp;fabrikat_nr=N&amp;auto_typ=T</c>: that group's sheets
///   (<c>detail_text</c> label, <c>detail_file</c> path without ".pdf") in one language -
///   land_id 1 = German, 2 = English. There is no "all groups" query (an empty or missing auto_typ
///   returns nothing), so discovery costs one request per model group (~75 for Opel).
///
/// Opel/Chevrolet/Cadillac/Saab use the German collection (every group has one). Vauxhall shares
/// make 1 with Opel - the collection is labelled "Opel/Vauxhall" and has no Vauxhall-specific model
/// names - so the Vauxhall instance serves the English collection of the same make, the one Vauxhall's
/// UK market uses (the same 157 records as the German one, translated). The model-group list is shared by the Opel and Vauxhall instances through
/// <see cref="DiscoveryResponseCache"/>.
///
/// Data quirks found on the real API: some labels carry stray line breaks (handled by
/// <see cref="IfzBerlinLabelParser"/>), and one English record points at another model's file ("Zafira
/// A (1999)" links <c>eng_opelvauxhall_zafira_life</c>, which "Zafira Life (2019)" links as well). A
/// file claimed by several records is kept once, for the record whose model/generation text actually
/// appears in the filename - otherwise the first one.
/// </summary>
public sealed class IfzBerlinRescueCardSource : RescueCardSourceBase
{
    private const string ApiBase = "https://ifz-berlin.de/proxy/";
    private const string PdfBase = "https://www.ifz-berlin.de/";
    private const string PortalUrl = "https://www.ifz-berlin.de/index.html";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly DiscoveryResponseCache _cache;
    private readonly ILogger<IfzBerlinRescueCardSource> _logger;
    private readonly int _fabrikatNr;
    private readonly int _landId;
    private readonly string _languageCode;

    public IfzBerlinRescueCardSource(
        Brand brand, IHttpClientFactory httpClientFactory, DiscoveryResponseCache cache, ILogger<IfzBerlinRescueCardSource> logger)
        : base(httpClientFactory)
    {
        (_fabrikatNr, _landId, _languageCode) = brand switch
        {
            Brand.Opel => (1, 1, "DE"),
            Brand.Vauxhall => (1, 2, "EN"),
            Brand.Chevrolet => (2, 1, "DE"),
            Brand.Cadillac => (3, 1, "DE"),
            Brand.Saab => (99, 1, "DE"),
            _ => throw new ArgumentOutOfRangeException(nameof(brand), brand, "IFZ Berlin only serves Opel, Vauxhall, Chevrolet, Cadillac and Saab.")
        };

        Brand = brand;
        _cache = cache;
        _logger = logger;
    }

    public override Brand Brand { get; }

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();

        // The model-group list failing (or not being JSON any more) is the "portal changed" signal.
        var typesJson = await _cache.GetStringAsync(client, $"{ApiBase}index_ret_8_3.php?fabrikat_nr={_fabrikatNr}", ct, AddReferer);
        var autoTypes = JsonSerializer.Deserialize<List<AutoTypDto>>(typesJson, JsonOptions) ?? [];

        var records = new List<(string AutoTyp, SheetDto Sheet, string DetailFile)>();
        foreach (var autoTyp in autoTypes.Select(t => t.AutoTyp).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct())
        {
            ct.ThrowIfCancellationRequested();
            var url = $"{ApiBase}index_ret_8_4.php?land_id={_landId}&fabrikat_nr={_fabrikatNr}&auto_typ={Uri.EscapeDataString(autoTyp!)}";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddReferer(request);
                using var response = await client.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();
                var sheets = JsonSerializer.Deserialize<List<SheetDto>>(await response.Content.ReadAsStringAsync(ct), JsonOptions) ?? [];
                records.AddRange(sheets
                    .Where(s => !string.IsNullOrWhiteSpace(s.DetailFile))
                    .Select(s => (autoTyp!, s, s.DetailFile!.Trim().TrimStart('/'))));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "{Message}", Strings.Get("RescueCards_Generic_PageReadFailed", Brand, url));
            }
        }

        var entries = new List<RescueCardEntry>();
        foreach (var group in records.GroupBy(r => r.DetailFile, StringComparer.OrdinalIgnoreCase))
        {
            var (autoTyp, sheet, detailFile) = PickRecordForFile(group.ToList());
            if (!RescueDocumentClassifier.IsRescueSheet(sheet.DetailText, detailFile))
            {
                continue;
            }

            var parsed = IfzBerlinLabelParser.Parse(autoTyp, sheet.DetailText, detailFile, _languageCode);
            var pdfUrl = HttpDownloadHelper.ResolveUrl(PdfBase, detailFile + ".pdf");
            entries.Add(new RescueCardEntry(Brand, PortalUrl, pdfUrl, detailFile, parsed));
        }

        _logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, entries.Count));
        return entries;
    }

    /// <summary>For a file several records point at, the record whose label (up to its first bracket)
    /// is spelled out in the filename - see the class comment - else the first.</summary>
    internal static (string AutoTyp, SheetDto Sheet, string DetailFile) PickRecordForFile(
        IReadOnlyList<(string AutoTyp, SheetDto Sheet, string DetailFile)> records)
    {
        if (records.Count == 1)
        {
            return records[0];
        }

        var normalizedFile = ModelNameNormalizer.Normalize(records[0].DetailFile);
        return records.FirstOrDefault(r =>
            ModelNameNormalizer.Normalize((r.Sheet.DetailText ?? string.Empty).Split('(')[0]) is { Length: > 0 } label &&
            normalizedFile.Contains(label, StringComparison.Ordinal),
            records[0]);
    }

    private static void AddReferer(HttpRequestMessage request) => request.Headers.Referrer = new Uri(PortalUrl);

    private sealed record AutoTypDto([property: JsonPropertyName("auto_typ")] string? AutoTyp);

    internal sealed record SheetDto(
        [property: JsonPropertyName("detail_text")] string? DetailText,
        [property: JsonPropertyName("detail_file")] string? DetailFile);
}
