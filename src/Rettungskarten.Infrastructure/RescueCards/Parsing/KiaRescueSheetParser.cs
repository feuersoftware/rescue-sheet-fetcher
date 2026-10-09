using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Metadata for Kia's rescue sheets. Kia's page gives every sheet a clean model heading ("Kia Ceed SW
/// Plug-in Hybrid", "Kia EV6 GT", "Kia PV5 Cargo") but only a "Herunterladen" button as link text,
/// and the filenames behind the buttons follow no single convention - newer ones the standard one
/// ("Kia_EV3_SUV_2024_5d_Electric_DE.pdf"), older ones anything from "rdb_kia_rio_5dr_2017.pdf" to
/// "Kia-Sportage-PHEV.pdf" or "Kia_Rettungsblatt_Niro_HEV_SUV_2022_5d_Electric_EN_RVSE_DE.pdf" (two
/// language tokens). So the model name comes from the heading and the remaining attributes are
/// *searched* for among the filename's tokens (year, doors, body, fuel, language in any position)
/// instead of being read positionally.
///
/// <see cref="SplitModelName"/> is shared with <c>KiaCombinedPdfLayout</c>, whose page headers use
/// Kia's older spellings ("cee‘d_sw", "pro_cee‘d", "Xceed PHEV").
/// </summary>
public static class KiaRescueSheetParser
{
    public sealed record ModelParts(string ModelName, string? BodyType, string? FuelType);

    private static readonly Regex BrandPrefix = new(@"^\s*Kia\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "cee‘d", "cee'd", "cee’d" (typographic apostrophes as printed) -> "Ceed"; "pro_cee‘d" -> "ProCeed".
    private static readonly Regex CeedSpelling = new(@"(pro_?)?cee[‘’'`´]?d", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Longest first; multi-word entries are matched as whole trailing word sequences.
    private static readonly string[] FuelWords = ["Plug-in Hybrid", "Mild Hybrid", "PHEV", "HEV", "Hybrid", "EV"];
    private static readonly string[] BodyWords = ["SW", "Fastback"];
    private static readonly string[] TrimWords = ["GT", "Cargo", "Passenger"];

    private static readonly Regex YearToken = new(@"^(?:19|20)\d{2}$", RegexOptions.Compiled);
    private static readonly Regex DoorsToken = new(@"^(\d)(?:d|dr)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, string> FilenameBodyTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hatchback"] = "Hatchback", ["Stationwagon"] = "Stationwagon", ["Sedan"] = "Sedan", ["SUV"] = "SUV",
        ["MPV"] = "MPV", ["Van"] = "Van", ["Coupe"] = "Coupe", ["CUV"] = "CUV"
    };

    private static readonly Dictionary<string, string> FilenameFuelTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PHEV"] = "PHEV", ["HEV"] = "HEV", ["Hybrid"] = "Hybrid", ["Electric"] = "Electric", ["EV"] = "EV", ["GD"] = "GD"
    };

    /// <summary>Parses one sheet from its page heading and file name. <paramref name="linkText"/> is the
    /// button text - Kia marks an English-only file there as "HERUNTERLADEN [EN]".</summary>
    public static ParsedModelInfo Parse(string heading, string fileName, string? linkText = null)
    {
        var name = BrandPrefix.Replace(heading, string.Empty).Trim();
        var parts = SplitModelName(name);

        var tokens = Path.GetFileNameWithoutExtension(fileName.TrimEnd('.'))
            .Split(['_', '-', ' ', '+', '(', ')'], StringSplitOptions.RemoveEmptyEntries);

        var year = tokens.Where(t => YearToken.IsMatch(t)).Select(int.Parse).Cast<int?>().FirstOrDefault();
        var doors = tokens.Select(t => DoorsToken.Match(t)).Where(m => m.Success).Select(m => (int?)int.Parse(m.Groups[1].Value)).FirstOrDefault();
        var body = parts.BodyType ?? tokens.Select(t => FilenameBodyTypes.GetValueOrDefault(t)).FirstOrDefault(b => b is not null);
        var fuel = parts.FuelType ?? tokens.Select(t => FilenameFuelTypes.GetValueOrDefault(t)).FirstOrDefault(f => f is not null);

        return new ParsedModelInfo(
            ModelName: parts.ModelName,
            Variant: name,
            BodyType: body,
            BuildYearFrom: year,
            BuildYearTo: null,
            Doors: doors,
            FuelType: fuel,
            LanguageCode: DetectLanguage(tokens, linkText),
            ParseConfidence: year is not null ? ParseConfidence.High : ParseConfidence.Heuristic);
    }

    /// <summary>The last language marker among the file name's tokens wins ("..._EN_RVSE_DE.pdf" is
    /// the German revision of an English original); a "[EN]" in the button text marks an English file
    /// too. Kia Germany's own files default to German.</summary>
    internal static string DetectLanguage(IReadOnlyList<string> tokens, string? linkText)
    {
        if (linkText is not null && linkText.Contains("[EN]", StringComparison.OrdinalIgnoreCase))
        {
            return "EN";
        }

        var marker = tokens.LastOrDefault(t => t is "DE" or "GER" or "EN" or "de" or "en");
        return marker is not null && marker.StartsWith("EN", StringComparison.OrdinalIgnoreCase) ? "EN" : "DE";
    }

    /// <summary>
    /// Splits a Kia model designation into the model name KBA knows and the drivetrain/body/trim words
    /// appended to it: "Ceed SW Plug-in Hybrid" -> Ceed (Sportswagon, Plug-in Hybrid), "Niro EV" ->
    /// Niro (EV), "EV6 GT" -> EV6, "cee‘d_sw" -> Ceed (Sportswagon), "pro_cee‘d" -> ProCeed.
    /// </summary>
    public static ModelParts SplitModelName(string name)
    {
        var text = CeedSpelling.Replace(name, m => m.Groups[1].Success ? "ProCeed" : "Ceed");
        text = Regex.Replace(text, @"_sw\b", " SW", RegexOptions.IgnoreCase).Replace('_', ' ');

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        string? body = null;
        string? fuel = null;

        var changed = true;
        while (changed && words.Count > 1)
        {
            changed = false;
            foreach (var (vocabulary, kind) in new[] { (FuelWords, 'f'), (BodyWords, 'b'), (TrimWords, 't') })
            {
                var match = vocabulary
                    .Select(v => v.Split(' '))
                    .Where(v => v.Length < words.Count && words.TakeLast(v.Length).SequenceEqual(v, StringComparer.OrdinalIgnoreCase))
                    .OrderByDescending(v => v.Length)
                    .FirstOrDefault();
                if (match is null)
                {
                    continue;
                }

                var value = string.Join(' ', words.TakeLast(match.Length));
                words.RemoveRange(words.Count - match.Length, match.Length);
                if (kind == 'f')
                {
                    fuel ??= value;
                }
                else if (kind == 'b')
                {
                    body ??= value.Equals("SW", StringComparison.OrdinalIgnoreCase) ? "Sportswagon" : value;
                }

                changed = true;
                break;
            }
        }

        return new ModelParts(string.Join(' ', words), body, fuel);
    }
}
