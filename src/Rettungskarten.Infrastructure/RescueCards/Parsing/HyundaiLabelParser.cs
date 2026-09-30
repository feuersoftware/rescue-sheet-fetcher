using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses Hyundai's rescue-sheet link labels ("Hyundai BAYON 48V-Hybrid: Rettungsdatenblatt (ab
/// 03/2021)", "Hyundai i30 Kombi: Rettungsdatenblatt (ab 2024)", "Hyundai INSTER: Rettungsdatenblatt
/// (09/2024)") and the per-model headers of Hyundai's combined "vor 11/2019" PDF ("Accent, 3-Türer",
/// "i30cw", "(Grand) Santa Fe", "Kona Elektro").
///
/// Hyundai names a drivetrain/trim/body variant by appending words to the model name ("KONA Hybrid",
/// "IONIQ 5 N", "SANTA FE Plug-in-Hybrid", "i30 Kombi") - the generic
/// <see cref="RescueSheetLabelParser"/> can't split those (it would cut "BAYON 48V-Hybrid" into
/// "BAYON 48V"), so this strips the known trailing variant words from the end instead and keeps the
/// full name as <see cref="ParsedModelInfo.Variant"/>. The model name keeps Hyundai's own spelling
/// ("IONIQ 5", "i20"): KBA's "IONIQ5"/"I 20" normalize to the same key.
/// </summary>
public static class HyundaiLabelParser
{
    private static readonly string[] FuelWords =
        ["48V-Hybrid", "Plug-in-Hybrid", "Plug-in Hybrid", "Hybrid", "HEV", "PHEV", "Elektro", "FCEV", "LPG"];

    private static readonly string[] BodyWords = ["Kombi", "Fastback", "Coupe", "Cargo", "Fahrgestell", "Bus"];

    // Performance trims written as a separate word after the model ("IONIQ 5 N").
    private static readonly string[] TrimWords = ["N", "N Line"];

    private static readonly Regex StationWagonSuffix = new(@"^(i\d0)cw$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BrandPrefix = new(@"^\s*Hyundai\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public sealed record ModelParts(string ModelName, string? BodyType, string? FuelType, int? Doors);

    /// <summary>Parses a download-list label from hyundai.com/de.</summary>
    public static ParsedModelInfo ParseLabel(string label)
    {
        var colon = label.IndexOf(':');
        var namePart = BrandPrefix.Replace(colon > 0 ? label[..colon] : label, string.Empty).Trim();
        var rest = colon > 0 ? label[(colon + 1)..] : string.Empty;

        var parts = SplitModelName(namePart);
        var years = ModelYearRangeTextHelper.Extract(rest, singleYearIsStartYear: true);

        return new ParsedModelInfo(
            ModelName: parts.ModelName,
            Variant: namePart,
            BodyType: parts.BodyType,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: parts.Doors,
            FuelType: parts.FuelType,
            LanguageCode: "DE",
            ParseConfidence: years.From is not null || years.To is not null ? ParseConfidence.High : ParseConfidence.Heuristic);
    }

    /// <summary>
    /// Splits "KONA Hybrid", "Accent, 3-Türer", "i30cw", "(Grand) Santa Fe" into the model name and
    /// the attributes appended to it. Comma-separated parts after the name are body/door details (the
    /// combined PDF's "Accent, 3-Türer", "H-350, Cargo").
    /// </summary>
    public static ModelParts SplitModelName(string name)
    {
        var commaParts = name.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var head = commaParts.Length > 0 ? commaParts[0] : name.Trim();
        var details = string.Join(' ', commaParts.Skip(1));

        // "(Grand) Santa Fe": the long-wheelbase sibling shares the sheet; the model is Santa Fe.
        head = Regex.Replace(head, @"^\(\s*Grand\s*\)\s*", string.Empty, RegexOptions.IgnoreCase);

        string? body = null;
        string? fuel = null;
        var words = head.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

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
                    body ??= value;
                }

                changed = true;
                break;
            }
        }

        var model = string.Join(' ', words);
        var wagon = StationWagonSuffix.Match(model);
        if (wagon.Success)
        {
            model = wagon.Groups[1].Value;
            body ??= "Kombi";
        }

        body ??= VehicleAttributeTextHelper.ExtractLastWordMatch(details, VehicleAttributeTextHelper.CommonBodyTypes)
            ?? BodyWords.FirstOrDefault(b => details.Contains(b, StringComparison.OrdinalIgnoreCase));

        return new ModelParts(model, body, fuel, VehicleAttributeTextHelper.ExtractDoors(details));
    }
}
