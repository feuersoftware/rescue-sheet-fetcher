using AngleSharp;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;

namespace Rettungskarten.Infrastructure.Stock;

public sealed record VehicleStockFetchResult(VehicleStockResult Parsed, byte[] RawContent, string RawFileName);

/// <summary>
/// Fetches the KBA FZ12 vehicle stock file for a given year. The download link's query-string
/// version parameter (<c>?__blob=publicationFile&amp;v=3</c>) changes unpredictably between years and
/// re-publications, so it is resolved by scraping the product catalog page rather than guessed.
/// </summary>
public sealed class KbaVehicleStockSource(
    IHttpClientFactory httpClientFactory,
    ILogger<KbaVehicleStockSource> logger) : IVehicleStockSource
{
    private const string ProductPageUrl =
        "https://www.kba.de/DE/Statistik/Produktkatalog/produkte/Fahrzeuge/fz12_b_uebersicht.html";
    private const string BaseUrl = "https://www.kba.de";

    public async Task<VehicleStockResult> FetchAsync(int year, CancellationToken ct) =>
        (await FetchWithRawAsync(year, ct)).Parsed;

    /// <summary>Downloads once and returns both the parsed result and the raw bytes, so callers that
    /// need to persist the original file (the CLI's `fetch stock` command) don't fetch it twice.</summary>
    public async Task<VehicleStockFetchResult> FetchWithRawAsync(int year, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(RettungskartenHttpClient.Name);

        var downloadUrl = await ResolveDownloadUrlAsync(client, year, ct)
            ?? throw new InvalidOperationException($"Kein Download-Link für FZ12 {year} auf der KBA-Produktseite gefunden.");

        if (!downloadUrl.Contains(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"FZ12 {year} liegt nicht im XLSX-Format vor ({downloadUrl}) - " +
                "ältere Formate (.xls/.pdf) werden derzeit nicht unterstützt.");
        }

        logger.LogInformation("Lade FZ12 {Year} von {Url}", year, downloadUrl);
        var bytes = await client.GetByteArrayAsync(downloadUrl, ct);
        var parsed = KbaStockXlsxParser.Parse(bytes, year, downloadUrl);

        foreach (var warning in parsed.UnparsedRowWarnings)
        {
            logger.LogWarning("FZ12 {Year}: {Warning}", year, warning);
        }

        var fileName = $"fz12_{year}{Path.GetExtension(new Uri(downloadUrl).AbsolutePath)}";
        return new VehicleStockFetchResult(parsed, bytes, fileName);
    }

    private static async Task<string?> ResolveDownloadUrlAsync(HttpClient client, int year, CancellationToken ct)
    {
        var html = await client.GetStringAsync(ProductPageUrl, ct);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var pattern = $"fz12_{year}";
        var link = document.QuerySelectorAll("a.c-publication")
            .FirstOrDefault(a => (a.GetAttribute("href") ?? string.Empty)
                .Contains(pattern, StringComparison.OrdinalIgnoreCase));

        var href = link?.GetAttribute("href");
        if (string.IsNullOrEmpty(href))
        {
            return null;
        }

        return href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : BaseUrl + href;
    }
}
