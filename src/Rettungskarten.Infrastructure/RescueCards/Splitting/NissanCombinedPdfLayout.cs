using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Layout of Nissan's combined "Nissan_Rettungsdatenblaetter_06_2019.pdf" (73 pages, no bookmarks -
/// verified): a contents page, then one sheet per model with a header "{MODEL}[ {body}] Typ: {code},
/// {years}" - "350Z Coupé Typ: Z33, 2002-2009", "NAVARA NAVARA King Cab Typ: D40, 2005-2015" (running
/// title printed twice), "NV250 NV250 L1 Typ: W, 2019-". The electric models' sheets continue on
/// pages without the header (high-voltage deactivation steps), so unkeyed pages join the sheet before
/// them; LEAF's second page repeats the header and merges into its first group. The contents page has
/// no "Typ:" and is skipped.
/// </summary>
public sealed class NissanCombinedPdfLayout : PageTextCombinedPdfLayout
{
    private const string Footer = "NISSAN CENTER EUROPE GMBH in Zusammenarbeit mit Moditech Rescue Solutions";

    private static readonly Regex Header = new(
        @"^(?<name>[^:]{1,50}?)\s+Typ:\s*(?<code>[A-Z0-9]+)\s*,\s*(?<years>(?:19|20)\d{2}\s*-\s*(?:(?:19|20)\d{2})?)",
        RegexOptions.Compiled);

    // Body/version words after the model name, longest first.
    private static readonly string[] VersionWords =
        ["Single Cab", "King Cab", "Crew Cab", "3-Türer", "4-Türer", "5-Türer", "Coupé", "Cabriolet", "Kombi", "C+C", "L1", "L2"];

    public override Brand Brand => Brand.Nissan;

    protected override bool UnkeyedPagesContinuePreviousGroup => true;

    protected override string? TryGetPageKey(string pageText)
    {
        var match = Header.Match(CombinedPdfHeaderText.StripFooter(pageText, Footer));
        if (!match.Success)
        {
            return null;
        }

        var name = CombinedPdfHeaderText.CollapseRepeatedPrefix(match.Groups["name"].Value.Trim());
        return $"{name} Typ: {match.Groups["code"].Value}, {LabelText.Collapse(match.Groups["years"].Value)}";
    }

    protected override ParsedModelInfo ParseGroup(string key, IReadOnlyList<string> pageTexts)
    {
        var match = Header.Match(key);
        var name = match.Groups["name"].Value.Trim();

        string? version = null;
        foreach (var word in VersionWords)
        {
            if (name.EndsWith(" " + word, StringComparison.OrdinalIgnoreCase))
            {
                version = word;
                name = name[..^(word.Length + 1)].Trim();
                break;
            }
        }

        var years = ModelYearRangeTextHelper.Extract(match.Groups["years"].Value);
        var body = version is null ? null
            : VehicleAttributeTextHelper.ExtractLastWordMatch(version, VehicleAttributeTextHelper.CommonBodyTypes) ?? version;

        return new ParsedModelInfo(
            ModelName: name,
            Variant: key,
            // Door counts and NV250 body lengths ("L1") are no body type.
            BodyType: version is not null && (version.EndsWith("-Türer", StringComparison.Ordinal) || version.StartsWith('L')) ? null : body,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: version is null ? null : VehicleAttributeTextHelper.ExtractDoors(version),
            FuelType: null,
            LanguageCode: "DE",
            ParseConfidence: years.From is not null ? ParseConfidence.Heuristic : ParseConfidence.Unparsed,
            ChassisCode: match.Groups["code"].Value);
    }
}
