using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Škoda's overview page and per-model pages both embed their real content as a JSON blob in a
/// <c>data-props</c> attribute (a client-hydration pattern), not as plain anchor tags in the static
/// DOM. The overview page's "OneColumnText" module's JSON has a <c>body</c> field that is itself an
/// HTML fragment containing the per-model links; each model page's "download-file" module JSON has a
/// structured <c>files</c> array with direct PDF links - no HTML-fragment parsing needed there.
/// The model name is taken from the overview page's link text (reliable), not re-derived from the
/// free-text file title, which only supplies the generation/variant label (e.g. "(2016-2021)").
/// </summary>
public sealed class SkodaRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<SkodaRescueCardSource> logger) : IRescueCardSource
{
    private const string OverviewUrl = "https://www.skoda-auto.de/service/rettungskraefte";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Every body style/fuel type actually seen in a real production run's model-page titles - see
    // VehicleAttributeTextHelper for why the *last* match in the title wins (e.g. "Kodiaq iV SUV 2024
    // 5d hybrid" names an "iV" trim before its actual fuel type, "hybrid", at the end).
    private static readonly string[] BodyTypeVocabulary = ["Combi", "Sedan", "Pick Up", "SUV"];

    // "iV" is excluded here and matched separately, case-sensitively, in ExtractFuelType below - it
    // collides with the Roman-numeral generation marker "IV" used throughout these same titles (e.g.
    // "Fabia IV", "Octavia IV"), which is always upper-case, unlike Škoda's lower-case-i electric-trim
    // badge (e.g. "Citigo-e iV", "Superb iV").
    private static readonly string[] FuelTypeVocabulary = ["PHEV HYBRID", "MHEV", "CNG", "LPG", "Hybrid", "GD"];

    // Case-sensitive by design - see the FuelTypeVocabulary comment above for why.
    private static readonly Regex ElectricTrimPattern = new(@"\biV\b", RegexOptions.Compiled);

    /// <summary>Extracts a fuel type from a Škoda model-page title, combining the general vocabulary
    /// match with the case-sensitive "iV" electric-trim badge and taking whichever occurs later in the
    /// text (e.g. "Superb iV PHEV HYBRID" - the more specific "PHEV HYBRID" wins over the earlier "iV";
    /// "Citigo-e iV" alone - "iV" is the only signal present, so it wins by default).</summary>
    internal static string? ExtractFuelType(string title)
    {
        var vocabularyMatch = VehicleAttributeTextHelper.ExtractLastVocabularyMatch(title, FuelTypeVocabulary);
        var electricTrimMatch = ElectricTrimPattern.Match(title);

        if (!electricTrimMatch.Success)
        {
            return vocabularyMatch;
        }

        if (vocabularyMatch is null)
        {
            return electricTrimMatch.Value;
        }

        var vocabularyMatchIndex = title.LastIndexOf(vocabularyMatch, StringComparison.Ordinal);
        return electricTrimMatch.Index > vocabularyMatchIndex ? electricTrimMatch.Value : vocabularyMatch;
    }

    public Brand Brand => Brand.Skoda;

    public async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(RettungskartenHttpClient.Name);
        var context = BrowsingContext.New(Configuration.Default);

        var overviewHtml = await client.GetStringAsync(OverviewUrl, ct);
        var overviewDoc = await context.OpenAsync(req => req.Content(overviewHtml), ct);

        var modelLinks = new List<(string ModelName, string PageUrl)>();
        foreach (var module in overviewDoc.QuerySelectorAll("div[data-module='OneColumnText']"))
        {
            var body = TryDeserialize<OneColumnTextEnvelope>(module.GetAttribute("data-props"))?.ViewModel?.Body;
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            var bodyDoc = await context.OpenAsync(req => req.Content(body), ct);
            foreach (var anchor in bodyDoc.QuerySelectorAll("a[href*='rettungsdatenblatt-']"))
            {
                var href = anchor.GetAttribute("href");
                var text = anchor.TextContent.Trim();
                if (!string.IsNullOrWhiteSpace(href) && !string.IsNullOrWhiteSpace(text))
                {
                    modelLinks.Add((text, HttpDownloadHelper.ResolveUrl(OverviewUrl, href)));
                }
            }
        }

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Skoda_ModelPagesFound", modelLinks.Count));

        var entries = new List<RescueCardEntry>();
        foreach (var (modelName, pageUrl) in modelLinks)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                entries.AddRange(await DiscoverModelPageAsync(client, context, modelName, pageUrl, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "{Message}", Strings.Get("RescueCards_Skoda_ModelPageReadFailed", pageUrl));
            }
        }

        return entries;
    }

    private static async Task<IReadOnlyList<RescueCardEntry>> DiscoverModelPageAsync(
        HttpClient client, IBrowsingContext context, string modelName, string pageUrl, CancellationToken ct)
    {
        var html = await client.GetStringAsync(pageUrl, ct);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var results = new List<RescueCardEntry>();
        foreach (var module in document.QuerySelectorAll("div[data-module='download-file']"))
        {
            var files = TryDeserialize<DownloadFileEnvelope>(module.GetAttribute("data-props"))?.ViewModel?.Files;
            foreach (var file in files ?? [])
            {
                if (string.IsNullOrWhiteSpace(file.Link))
                {
                    continue;
                }

                var title = file.Title?.Trim();
                var titleOrEmpty = title ?? string.Empty;
                var yearRange = ModelYearRangeTextHelper.Extract(titleOrEmpty);
                var parsed = new ParsedModelInfo(
                    ModelName: modelName,
                    Variant: title,
                    BodyType: VehicleAttributeTextHelper.ExtractLastVocabularyMatch(titleOrEmpty, BodyTypeVocabulary),
                    BuildYearFrom: yearRange.From,
                    BuildYearTo: yearRange.To,
                    Doors: VehicleAttributeTextHelper.ExtractDoors(titleOrEmpty),
                    FuelType: ExtractFuelType(titleOrEmpty),
                    LanguageCode: "DE",
                    ParseConfidence: yearRange.From is null && yearRange.To is null
                        ? ParseConfidence.Unparsed
                        : ParseConfidence.Heuristic);

                results.Add(new RescueCardEntry(Brand.Skoda, pageUrl, file.Link, title ?? modelName, parsed));
            }
        }

        return results;
    }

    public async Task<RescueCardDownloadResult> DownloadAsync(RescueCardEntry entry, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(RettungskartenHttpClient.Name);
        return await HttpDownloadHelper.DownloadPdfAsync(client, entry.DownloadUrl!, ct);
    }

    private static T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record OneColumnTextEnvelope(OneColumnTextViewModel? ViewModel);
    private sealed record OneColumnTextViewModel(string? Body);
    private sealed record DownloadFileEnvelope(DownloadFileViewModel? ViewModel);
    private sealed record DownloadFileViewModel(List<DownloadFileEntry>? Files);
    private sealed record DownloadFileEntry(string? Title, string? Link);
}
