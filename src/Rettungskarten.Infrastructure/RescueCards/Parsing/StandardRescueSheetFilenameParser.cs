using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses the industry-wide underscore-delimited rescue-sheet filename convention (the Euro Rescue
/// naming scheme manufacturers publish their own files under), e.g.
/// "Volkswagen_Arteon_eHYBRID_Coupe_2020_5d_Hybrid-Electric_DE.pdf" or
/// "Cupra_Terramar__SUV_2024_5d_Hybrid_DE.pdf" (double underscores from an empty variant token are
/// collapsed). Seen as-is on VW, SEAT, Cupra, Mercedes, BMW (partly), Renault, Dacia, Nissan, Kia,
/// Volvo, the Stellantis Servicebox, Alfa Romeo, MG and smart. See
/// <see cref="PositionalFilenameParser"/> for the actual field-extraction rules and convention shape.
///
/// Sources should call <see cref="TryParse"/> first and fall back to their own label/link-text parsing
/// when it returns false - a filename that doesn't follow the convention produces an
/// <see cref="ParseConfidence.Unparsed"/> result whose positional fields would be garbage.
///
/// Audi uses its own <see cref="AudiFilenameParser"/> instead, not this one: its CMS emits filename
/// quirks (a fuel type split across two tokens, a stray de-duplication numeral) that no other brand's
/// filenames have been seen with, so normalizing for them here would only add untested surface area
/// to every brand whose parsing already works correctly.
/// </summary>
public static class StandardRescueSheetFilenameParser
{
    public static ParsedModelInfo Parse(string fileNameOrUrl) =>
        PositionalFilenameParser.Parse(PositionalFilenameParser.Tokenize(fileNameOrUrl));

    /// <summary>True (with the result) when the filename follows the convention well enough to trust
    /// - i.e. the result isn't <see cref="ParseConfidence.Unparsed"/>.</summary>
    public static bool TryParse(string fileNameOrUrl, out ParsedModelInfo parsed)
    {
        parsed = Parse(fileNameOrUrl);
        return parsed.ParseConfidence != ParseConfidence.Unparsed;
    }
}
