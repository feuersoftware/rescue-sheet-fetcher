using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Layout of Kia's combined "Rettungsdatenblätter ältere Modelle" PDF
/// (Kia_Rettungsdatenblaetter_08_2020+Sorento_GER.pdf, 58 pages, no bookmarks - verified): a notice
/// page, then exactly one page per sheet, each with a header "{model} ({code}) {body/years}" -
/// "Carnival (GQ) 1999-2006", "cee‘d_sw (ED) Sporty Wagon, 2007-2012", "Niro (DE HEV) ab MY 2018- 12V
/// in HV Batterie" - next to the running title, the revision date and the publisher footer (see
/// <see cref="CombinedPdfHeaderText"/>). The header text itself is the key: two sheets of the same
/// model and code differ in their years/body text ("Optima (JF) 4-Türer ab 2016" / "Optima (JF)
/// 5-Türer ab 2016").
/// </summary>
public sealed class KiaCombinedPdfLayout : PageTextCombinedPdfLayout
{
    private const string Footer = "KIA Motors Deutschland GmbH in Zusammenarbeit mit Moditech Rescue Solutions";

    private static readonly Regex Header = new(
        @"^(?<name>[^()]{1,60}?)\s*\((?<code>[A-Z0-9][A-Z0-9 /\-]{0,12})\)\s*(?<rest>.*)$", RegexOptions.Compiled);

    // Page furniture printed after the header text on a few sheets.
    private static readonly Regex TrailingNoise = new(@"\s*(?:inklusive Facelift|Fußgänger-\s*schutz-\s*system).*$", RegexOptions.Compiled);

    private static readonly IReadOnlyList<string> BodyTypes =
        [.. VehicleAttributeTextHelper.CommonBodyTypes, "Sportswagon", "Sporty Wagon"];

    public override Brand Brand => Brand.Kia;

    protected override string? TryGetPageKey(string pageText)
    {
        if (!pageText.Contains("Moditech", StringComparison.OrdinalIgnoreCase))
        {
            return null; // the leading notice page
        }

        var header = Header.Match(CombinedPdfHeaderText.StripFooter(pageText, Footer));
        if (!header.Success)
        {
            return null;
        }

        var name = CombinedPdfHeaderText.CollapseRepeatedPrefix(header.Groups["name"].Value.Trim());
        var rest = TrailingNoise.Replace(header.Groups["rest"].Value, string.Empty).Trim();

        // Some pages print the running title after the header instead of before it ("Soul EV (PS EV)
        // 2014-2019 Soul EV").
        if (rest.EndsWith(" " + name, StringComparison.OrdinalIgnoreCase))
        {
            rest = rest[..^(name.Length + 1)].TrimEnd();
        }

        return $"{name} ({header.Groups["code"].Value.Trim()}) {rest}".Trim();
    }

    protected override ParsedModelInfo ParseGroup(string key, IReadOnlyList<string> pageTexts)
    {
        var header = Header.Match(key);
        var name = header.Groups["name"].Value.Trim();
        var rest = header.Groups["rest"].Value;

        var parts = KiaRescueSheetParser.SplitModelName(name);
        var years = ModelYearRangeTextHelper.Extract(rest, singleYearIsStartYear: true);
        var body = parts.BodyType
            ?? VehicleAttributeTextHelper.ExtractLastWordMatch(rest, BodyTypes);
        var fuel = parts.FuelType
            ?? VehicleAttributeTextHelper.ExtractLastWordMatch(rest, VehicleAttributeTextHelper.CommonFuelTypes);

        return new ParsedModelInfo(
            ModelName: parts.ModelName,
            Variant: key,
            BodyType: body,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: VehicleAttributeTextHelper.ExtractDoors(rest),
            FuelType: fuel,
            LanguageCode: "DE",
            ParseConfidence: years.From is not null || years.To is not null ? ParseConfidence.Heuristic : ParseConfidence.Unparsed,
            ChassisCode: header.Groups["code"].Value.Split(' ')[0]);
    }
}
