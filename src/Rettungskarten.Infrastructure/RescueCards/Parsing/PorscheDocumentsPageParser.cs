using System.Text.Json;
using AngleSharp;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Extracts "Rescue Data Sheets" document links from Porsche's "Further Documents" page. The page's
/// document list isn't in the parsed DOM at all: it's Vue SSR markup embedded verbatim inside a
/// &lt;script type="text/x-template" id="app-template"&gt; block. Per the HTML5 spec, a browser (and
/// AngleSharp) never parses &lt;script&gt; contents as markup - they're raw text - so that block's
/// TextContent has to be re-parsed as its own standalone document before the
/// &lt;link-list :items="[...]"&gt; custom elements inside it become queryable. Each `:items`
/// attribute then holds a JSON-like array using single quotes instead of double quotes; none of this
/// page's actual link text/URLs contain an apostrophe, so a straight quote substitution reliably
/// turns it into parseable JSON.
///
/// In 2026 Porsche rebuilt the page with Astro: the same links are now plain server-rendered anchors
/// (<c>a.link-list-item</c>, label in <c>p.link-list-text</c>) served from files.porsche.com. Both
/// layouts are supported - the template path first, the plain anchors when it finds nothing - so a
/// partial rollout or a rollback of the rebuild doesn't break discovery again.
/// </summary>
public static class PorscheDocumentsPageParser
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<IReadOnlyList<(string Text, string Href)>> ParseRescueDataSheetLinksAsync(
        string html, CancellationToken ct)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var templateScript = document.QuerySelectorAll("script[type='text/x-template']").FirstOrDefault();
        if (templateScript is null)
        {
            return ParsePlainAnchors(document);
        }

        var templateDocument = await context.OpenAsync(req => req.Content(templateScript.TextContent), ct);

        var results = new List<(string, string)>();
        foreach (var linkList in templateDocument.QuerySelectorAll("link-list"))
        {
            var itemsJson = linkList.GetAttribute(":items");
            if (string.IsNullOrWhiteSpace(itemsJson))
            {
                continue;
            }

            List<LinkListItem>? items;
            try
            {
                items = JsonSerializer.Deserialize<List<LinkListItem>>(itemsJson.Replace('\'', '"'), JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            foreach (var item in items ?? [])
            {
                var text = item.Link?.Text?.Trim();
                var href = item.Link?.Href;
                if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(href) ||
                    !text.StartsWith("Rescue Data Sheets", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add((text, href));
            }
        }

        return results.Count > 0 ? results : ParsePlainAnchors(document);
    }

    private static List<(string Text, string Href)> ParsePlainAnchors(AngleSharp.Dom.IDocument document) =>
        document.QuerySelectorAll("a[href]")
            .Select(a => (
                Text: (a.QuerySelector(".link-list-text")?.TextContent ?? a.TextContent).Trim(),
                Href: a.GetAttribute("href") ?? string.Empty))
            .Where(l => l.Text.StartsWith("Rescue Data Sheets", StringComparison.OrdinalIgnoreCase) &&
                l.Href.Split('?')[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(l => l.Href)
            .ToList();

    private sealed record LinkListItem(LinkInfo? Link);
    private sealed record LinkInfo(string? Href, string? Text);
}
