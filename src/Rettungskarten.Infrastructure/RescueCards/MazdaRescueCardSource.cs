using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// mazda.de/rettungskarten/ renders its content client-side from JSON the server embeds in the page:
/// every content block is a <c>window.mxp.data.push(JSON.parse('…'))</c> script with the block's JSON
/// as a JavaScript single-quoted string literal (so the JSON's own escapes are escaped once more -
/// "\\u003c" for "&lt;", "\\\"" for a quote). The rescue cards are one block of type "faq" titled
/// "ALLE RETTUNGSKARTEN": one FAQ item per model ("Mazda CX-5", "Mazda2 Hybrid"), whose "content" is
/// an HTML list with one <c>&lt;li&gt;</c> per sheet. So discovery unescapes the JS literal, parses the
/// JSON, and parses each FAQ item's HTML - no headless browser needed.
///
/// Within a list item the sheet's label ("Mazda CX-5 | KE | 2012 - 2017") is usually the link text
/// itself, but for newer models it's the text *in front of* a bare "&gt; PDF herunterladen" link
/// (CX-60, CX-80, Mazda2 Hybrid, Mazda6e); a nested list under it holds VIN ranges. The label is
/// therefore the list item's own text without its nested lists. See <see cref="MazdaLabelParser"/>
/// for how the label is taken apart.
///
/// PDFs are on Mazda's Cloudinary host (media-assets.mazda.eu) with a cache-busting "?rnd=" query;
/// the query is kept in the download URL (it's what the page links) but not in the id-hash input,
/// which is the filename. A few newer sheets exist only in English and are kept as "EN" entries;
/// where Mazda offers a German and an English sheet for the same generation, German wins.
/// </summary>
public sealed class MazdaRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<MazdaRescueCardSource> logger)
    : RescueCardSourceBase(httpClientFactory)
{
    private const string PageUrl = "https://www.mazda.de/rettungskarten/";

    private static readonly Regex DataBlock = new(
        @"window\.mxp\.data\.push\(JSON\.parse\('(?<json>(?:[^'\\]|\\.)*)'\)\)", RegexOptions.Compiled);

    private static readonly Regex JsEscape = new(@"\\(?:u(?<hex>[0-9a-fA-F]{4})|x(?<hex>[0-9a-fA-F]{2})|(?<ch>.))", RegexOptions.Compiled | RegexOptions.Singleline);

    public override Brand Brand => Brand.Mazda;

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();
        var html = await client.GetStringAsync(PageUrl, ct);

        var faqs = ExtractRescueCardFaqs(html);
        if (faqs.Count == 0)
        {
            logger.LogWarning("{Message}", Strings.Get("RescueCards_Generic_CardListNotFound", Brand, PageUrl));
            return [];
        }

        var context = BrowsingContext.New(Configuration.Default);
        var entries = new List<RescueCardEntry>();
        var usedRawNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (title, contentHtml) in faqs)
        {
            var document = await context.OpenAsync(req => req.Content($"<body>{contentHtml}</body>").Address(PageUrl), ct);
            foreach (var anchor in document.QuerySelectorAll("a[href]"))
            {
                var href = anchor.GetAttribute("href")!;
                string absoluteUrl;
                try
                {
                    absoluteUrl = HttpDownloadHelper.ResolveUrl(PageUrl, href);
                }
                catch (UriFormatException)
                {
                    continue;
                }

                var fileName = HttpDownloadHelper.GetFileName(absoluteUrl);
                if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var label = LabelOf(anchor);
                if (!RescueDocumentClassifier.IsRescueSheet(label, fileName))
                {
                    continue;
                }

                var parsed = MazdaLabelParser.Parse(title, label, fileName);
                var rawName = usedRawNames.Add(fileName) ? fileName : absoluteUrl;
                entries.Add(new RescueCardEntry(Brand, PageUrl, absoluteUrl, rawName, parsed));
            }
        }

        var preferred = LanguagePreference.PreferGermanThenEnglish(
            entries,
            e => $"{e.Parsed.ModelName}|{e.Parsed.ChassisCode}|{e.Parsed.BuildYearFrom}|{e.Parsed.BodyType}|{e.Parsed.FuelType}",
            e => e.Parsed.LanguageCode);
        logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, preferred.Count));
        return preferred;
    }

    /// <summary>(FAQ item title, item HTML) of every item in the page's rescue-card FAQ block(s).</summary>
    internal static IReadOnlyList<(string Title, string Html)> ExtractRescueCardFaqs(string pageHtml)
    {
        var result = new List<(string, string)>();
        foreach (Match block in DataBlock.Matches(pageHtml))
        {
            var literal = block.Groups["json"].Value;
            // Cheap pre-filter: only the FAQ blocks are worth unescaping and parsing (the page has
            // dozens of blocks, the header/footer ones are >100 KB each).
            if (!literal.Contains("\"faqs\"", StringComparison.Ordinal) ||
                !literal.Contains("rettungskarte", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(UnescapeJsString(literal));
            }
            catch (JsonException)
            {
                continue;
            }

            using (json)
            {
                if (!json.RootElement.TryGetProperty("props", out var props) ||
                    !props.TryGetProperty("faqs", out var faqList) || faqList.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var faq in faqList.EnumerateArray())
                {
                    var title = faq.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var content = faq.TryGetProperty("content", out var c) ? c.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(content) &&
                        content.Contains(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add((title.Trim(), content));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Undoes JavaScript string-literal escaping (the JSON inside keeps its own escapes, which
    /// the JSON parser then resolves).</summary>
    internal static string UnescapeJsString(string literal) =>
        JsEscape.Replace(literal, m =>
        {
            if (m.Groups["hex"].Success)
            {
                return ((char)Convert.ToInt32(m.Groups["hex"].Value, 16)).ToString();
            }

            return m.Groups["ch"].Value switch
            {
                "n" => "\n",
                "r" => "\r",
                "t" => "\t",
                var other => other
            };
        });

    /// <summary>The enclosing list item's own text (without nested VIN lists), or the link text when
    /// the link isn't in a list.</summary>
    private static string LabelOf(IElement anchor)
    {
        var item = anchor.Closest("li");
        if (item is null)
        {
            return anchor.TextContent;
        }

        var text = new StringBuilder();
        foreach (var node in item.ChildNodes)
        {
            if (node is IElement { LocalName: "ul" or "ol" })
            {
                continue;
            }

            text.Append(' ').Append(node.TextContent);
        }

        return text.ToString().Replace(' ', ' ');
    }
}
