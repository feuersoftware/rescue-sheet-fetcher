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
            return [];
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

        return results;
    }

    private sealed record LinkListItem(LinkInfo? Link);
    private sealed record LinkInfo(string? Href, string? Text);
}
