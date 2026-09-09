using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses the shared underscore-delimited filename convention used by VW, SEAT, and Cupra(.ch) rescue
/// card PDFs, e.g. "Volkswagen_Arteon_eHYBRID_Coupe_2020_5d_Hybrid-Electric_DE.pdf" or
/// "Cupra_Terramar__SUV_2024_5d_Hybrid_DE.pdf" (double underscores from an empty variant token are
/// collapsed). See <see cref="PositionalFilenameParser"/> for the actual field-extraction rules and
/// convention shape - this class is just these three brands' un-normalized entry point into it.
///
/// Audi uses its own <see cref="AudiFilenameParser"/> instead, not this one: its CMS emits filename
/// quirks (a fuel type split across two tokens, a stray de-duplication numeral) that none of VW, SEAT,
/// or Cupra's filenames ever have, so normalizing for them here would only add untested surface area
/// to three brands whose parsing already works correctly.
/// </summary>
public static class VwSeatCupraFilenameParser
{
    public static ParsedModelInfo Parse(string fileNameOrUrl) =>
        PositionalFilenameParser.Parse(PositionalFilenameParser.Tokenize(fileNameOrUrl));
}
