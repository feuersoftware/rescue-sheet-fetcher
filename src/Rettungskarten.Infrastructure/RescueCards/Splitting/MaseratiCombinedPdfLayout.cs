using System.Globalization;
using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Layout of Maserati Germany's combined rescue-sheet PDF ("Maserati_Rettungsdatenblatter_5_2016.pdf",
/// see <see cref="Stellantis.MaseratiRescueCardSource"/>): six pages, exactly one per model, no
/// bookmarks (verified). Each page states its model in capitals with the build-year range next to the
/// footer - "GRANCABRIO (2010-)", "QUATTROPORTE (2004-2012)", "QUATTROPORTE (2013-)" - where PdfPig puts
/// it either before or after the "02/2014 | Maserati Deutschland GmbH in Zusammenarbeit mit Moditech
/// Rescue Solutions B.V." footer, so it is searched anywhere in the page text. The model name plus
/// start year is the key: the two Quattroporte generations share the name.
/// </summary>
public sealed class MaseratiCombinedPdfLayout : PageTextCombinedPdfLayout
{
    private static readonly Regex ModelHeader = new(
        @"(?<![\p{L}])([A-Z][A-Z]+(?: [A-Z]+)*) \(((?:19|20)\d{2})\s*-\s*((?:19|20)\d{2})?\)",
        RegexOptions.Compiled);

    /// <summary>Marketing spelling for the capitalized names the sheets use.</summary>
    private static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GRANTURISMO"] = "GranTurismo",
        ["GRANCABRIO"] = "GranCabrio"
    };

    public override Brand Brand => Brand.Maserati;

    protected override string? TryGetPageKey(string pageText)
    {
        var match = ModelHeader.Match(pageText);
        return match.Success ? $"{match.Groups[1].Value} ({match.Groups[2].Value}-{match.Groups[3].Value})" : null;
    }

    protected override ParsedModelInfo ParseGroup(string key, IReadOnlyList<string> pageTexts)
    {
        var match = ModelHeader.Match(key);
        var name = match.Groups[1].Value;
        var modelName = KnownNames.TryGetValue(name, out var known)
            ? known
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());

        return new ParsedModelInfo(
            ModelName: modelName, Variant: key, BodyType: null,
            BuildYearFrom: int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            BuildYearTo: match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : null,
            Doors: null, FuelType: null, LanguageCode: "DE", ParseConfidence.Heuristic);
    }
}
