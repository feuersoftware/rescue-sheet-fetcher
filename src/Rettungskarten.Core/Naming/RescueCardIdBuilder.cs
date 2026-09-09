using System.Security.Cryptography;
using System.Text;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Naming;

/// <summary>
/// Builds deterministic, filesystem-safe identifiers for rescue cards. A short content hash is always
/// appended because per-brand filename/text parsing is only ever a best effort — two distinct source
/// entries can parse to the same human-readable slug (e.g. two Škoda generations of the same model),
/// and the id must still be unique and stable across repeated runs of the same source data.
/// </summary>
public static class RescueCardIdBuilder
{
    public static string BuildModelFolderSlug(string? modelName) =>
        Slugify(string.IsNullOrWhiteSpace(modelName) ? "unknown" : modelName);

    public static string BuildId(Brand brand, ParsedModelInfo parsed, string rawFileNameOrLabel)
    {
        var parts = new List<string> { brand.ToString().ToLowerInvariant(), BuildModelFolderSlug(parsed.ModelName) };

        if (parsed.BuildYearFrom is { } year)
        {
            parts.Add(year.ToString());
        }

        if (!string.IsNullOrWhiteSpace(parsed.BodyType))
        {
            parts.Add(Slugify(parsed.BodyType));
        }

        if (!string.IsNullOrWhiteSpace(parsed.LanguageCode))
        {
            parts.Add(Slugify(parsed.LanguageCode));
        }

        parts.Add(ShortHash(rawFileNameOrLabel));
        return string.Join('-', parts);
    }

    private static string Slugify(string value)
    {
        var sb = new StringBuilder(value.Length);
        var lastWasDash = false;

        foreach (var c in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash && sb.Length > 0)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }

        while (sb.Length > 0 && sb[^1] == '-')
        {
            sb.Length--;
        }

        return sb.Length == 0 ? "unknown" : sb.ToString();
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes)[..8];
    }
}
