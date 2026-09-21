using System.Globalization;
using System.Text;

namespace Rettungskarten.Core.Matching;

/// <summary>
/// Normalizes model/brand names for fuzzy matching across sources that never agree on casing,
/// punctuation, or diacritics (e.g. rescue-card "ID.4" vs. KBA "ID 4", "Škoda" vs. "SKODA").
/// </summary>
public static class ModelNameNormalizer
{
    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToUpperInvariant(c));
            }
        }

        return sb.ToString();
    }
}
