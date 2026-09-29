using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// RUF Automobile's rescue-card page (ruf-automobile.de/de/rettungskarten/, a ProcessWire site) has
/// three buttons with direct PDF links, one per electric RUF: "eRUF Variante 1", "eRUF Variante 3"
/// and "eRUF Stromster" - RUF only publishes rescue cards for its electric conversions (its
/// Porsche-based combustion cars are covered by Porsche's sheets). The button labels name no years.
/// "Variante N" is a variant of the eRUF (Model A), so it goes to <see cref="ParsedModelInfo.Variant"/>;
/// the Stromster is its own model. RUF isn't in KBA's FZ12 (below the publication threshold).
/// </summary>
public sealed class RufRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<RufRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    internal const string PageUrl = "https://www.ruf-automobile.de/de/rettungskarten/";

    private static readonly Regex VariantSuffix = new(@"\s+Variante\s+\d+\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override Brand Brand => Brand.Ruf;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) => ParseLabel(label);

    internal static ParsedModelInfo ParseLabel(string label)
    {
        var model = VariantSuffix.Replace(label, string.Empty).Trim();
        return new ParsedModelInfo(
            ModelName: model.Length > 0 ? model : null, Variant: label, BodyType: null,
            BuildYearFrom: null, BuildYearTo: null, Doors: null, FuelType: "Elektro", LanguageCode: "DE",
            ParseConfidence: model.Length > 0 ? ParseConfidence.Heuristic : ParseConfidence.Unparsed);
    }
}
