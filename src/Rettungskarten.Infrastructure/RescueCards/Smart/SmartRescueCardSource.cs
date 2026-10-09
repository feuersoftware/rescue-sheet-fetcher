using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.Smart;

/// <summary>
/// smart's own rescue-card site, rescuecard.smart.com, for the models of the smart Automobile joint
/// venture (Mercedes-Benz/Geely, since 2022): #1, #3, #5. The older, Mercedes-built smart generations
/// (fortwo/forfour/roadster) are on Mercedes' portal instead and come from
/// <see cref="Mercedes.MercedesRescueCardSource"/> registered for <see cref="Brand.Smart"/> - both
/// sources run for the same brand, and only this one's entries carry
/// <see cref="ManufacturerGroup.Geely"/> as their group (the brand's default is Mercedes-Benz Group).
///
/// Found on the real site (2026-09-29): an Angular single-page app with no API and no server-side
/// rendering (the index page is an empty &lt;app-root&gt;; robots.txt is answered with that same
/// index page, i.e. there are no rules). The model catalogue lives only in the compiled bundle, so
/// discovery fetches the index page, follows its <c>main.&lt;hash&gt;.js</c> reference and reads the
/// model list from it (see <see cref="SmartAppBundleParser"/>). The app builds each PDF URL as
/// <c>assets/pdfs/&lt;key&gt;/</c> + <c>encodeURIComponent(prefix + language + ".pdf")</c>; the
/// filenames contain a literal '#' ("smart_#1__SUV_2022_5d_Electric_DE.pdf"), which has to be sent
/// as %23 or everything after it is dropped as a URL fragment. A file that doesn't exist is answered
/// with 200 and the app's HTML page, which the download helper's %PDF check rejects.
///
/// Filenames follow the industry convention, so <see cref="StandardRescueSheetFilenameParser"/> reads
/// the metadata; the model name "#1" normalizes to KBA's series "1" (FZ12 lists them as "SMART 1").
/// </summary>
public sealed class SmartRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<SmartRescueCardSource> logger)
    : RescueCardSourceBase(httpClientFactory)
{
    public const string SiteUrl = "https://rescuecard.smart.com/";

    public override Brand Brand => Brand.Smart;

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();
        var indexHtml = await client.GetStringAsync(SiteUrl, ct);

        var bundlePath = SmartAppBundleParser.FindMainBundlePath(indexHtml)
            ?? throw new InvalidOperationException(Strings.Get("RescueCards_Smart_BundleNotFound", SiteUrl));
        var bundleUrl = HttpDownloadHelper.ResolveUrl(SiteUrl, bundlePath);
        var bundle = await client.GetStringAsync(bundleUrl, ct);

        var models = SmartAppBundleParser.ParseModels(bundle);
        if (models.Count == 0)
        {
            throw new InvalidOperationException(Strings.Get("RescueCards_Smart_ModelListNotFound", bundleUrl));
        }

        var entries = new List<RescueCardEntry>();
        foreach (var model in models)
        {
            // German if offered, English otherwise; an unreadable language list is treated as
            // "German offered" - every model so far has it, and a wrong guess only fails that download.
            var language = model.Languages.Count == 0 || model.Languages.Contains("DE") ? "DE"
                : model.Languages.Contains("EN") ? "EN"
                : null;
            if (language is null)
            {
                continue;
            }

            var fileName = model.FileNamePrefix + language + ".pdf";
            var escapedFileName = Uri.EscapeDataString(fileName);
            var pdfUrl = HttpDownloadHelper.ResolveUrl(SiteUrl, $"assets/pdfs/{Uri.EscapeDataString(model.Key)}/{escapedFileName}");

            // Parsed from the escaped name: the tokenizer treats a literal '#' as the start of a fragment.
            var parsed = StandardRescueSheetFilenameParser.Parse(escapedFileName);
            parsed = parsed with { LanguageCode = parsed.LanguageCode ?? language };

            entries.Add(new RescueCardEntry(
                Brand, HttpDownloadHelper.ResolveUrl(SiteUrl, model.Key), pdfUrl, fileName, parsed,
                ManufacturerGroupOverride: ManufacturerGroup.Geely));
        }

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, entries.Count));
        return entries;
    }
}
