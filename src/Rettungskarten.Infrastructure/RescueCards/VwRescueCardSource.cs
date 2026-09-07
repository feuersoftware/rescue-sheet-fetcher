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
/// list), bucketed by language. Actually downloading a PDF from the feed's base_url returned HTTP 403
/// in research (likely a signed-URL/session requirement not yet identified) - discovery still succeeds
/// so every model's metadata is captured, but downloads are expected to fail and land as MetadataOnly.
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

        if (string.IsNullOrEmpty(baseUrl) || germanBucket?.Files is null)
        {
            logger.LogWarning("{Message}", Strings.Get("RescueCards_Vw_LanguageBucketNotFound"));
            return [];
        }

        var entries = new List<RescueCardEntry>(germanBucket.Files.Count);
        foreach (var fileName in germanBucket.Files)
        {
            var downloadUrl = baseUrl.TrimEnd('/') + "/" + fileName;
            var parsed = VwAudiCupraFilenameParser.Parse(fileName);
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
