using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// VW's model-search widget loads its data from a public JSON feed (no auth needed to read the file
/// list), bucketed by language. The PDF's actual S3 key is <c>{base_url}/{languageBucketName}/{fileName}</c>,
/// not <c>{base_url}/{fileName}</c> - the feed's flat file list omits that path segment, and the bucket
/// returns a generic "AccessDenied" (not "NoSuchKey") for the wrong path, which made this look like an
/// auth/session problem rather than the URL-construction bug it actually is. Found by intercepting the
/// network request VW's own rescue-data widget makes (volkswagen.de/de/besitzer-und-service/ueber-ihr-auto/
/// kundeninformationen/rechtliches/rescue-data.html) when downloading a card manually.
/// </summary>
public sealed class VwRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<VwRescueCardSource> logger) : IRescueCardSource
{
    private const string FeedUrl = "https://assets.feature-app.io/rescue-asset/vw-de/config/rescueEntries.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Brand Brand => Brand.VW;

    public async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(RettungskartenHttpClient.Name);
        var json = await client.GetStringAsync(FeedUrl, ct);
        var root = JsonSerializer.Deserialize<RescueEntriesRoot>(json, JsonOptions);

        var baseUrl = root?.Config?.BaseUrl;
        var germanBucket = root?.Languages?.FirstOrDefault(l => (l.Name ?? string.Empty).Contains(".DE", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(baseUrl) || germanBucket?.Files is null || string.IsNullOrEmpty(germanBucket.Name))
        {
            logger.LogWarning("{Message}", Strings.Get("RescueCards_Vw_LanguageBucketNotFound"));
            return [];
        }

        var entries = new List<RescueCardEntry>(germanBucket.Files.Count);
        foreach (var fileName in germanBucket.Files)
        {
            var downloadUrl = $"{baseUrl.TrimEnd('/')}/{germanBucket.Name}/{fileName}";
            var parsed = VwSeatCupraFilenameParser.Parse(fileName);
            entries.Add(new RescueCardEntry(Brand.VW, FeedUrl, downloadUrl, fileName, parsed));
        }

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Vw_DiscoveredCount", entries.Count));

        return entries;
    }

    public async Task<RescueCardDownloadResult> DownloadAsync(RescueCardEntry entry, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(RettungskartenHttpClient.Name);
        return await HttpDownloadHelper.DownloadPdfAsync(client, entry.DownloadUrl!, ct);
    }

    private sealed record RescueEntriesRoot(RescueConfig? Config, List<RescueLanguageBucket>? Languages);
    private sealed record RescueConfig([property: JsonPropertyName("base_url")] string? BaseUrl);
    private sealed record RescueLanguageBucket(string? Name, List<string>? Files);
}
