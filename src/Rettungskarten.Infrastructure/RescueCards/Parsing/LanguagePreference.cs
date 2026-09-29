namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// The tool's language rule for sources that offer one model's sheet in several languages: German if
/// there is one; English only as a fallback for a model that has no German sheet at all (Tesla's
/// Model Y, a few Mazda/Kia sheets - the same situation Lamborghini/Porsche are in for every model);
/// any other language never. "Model" is whatever key the caller groups by - it must identify one
/// model variant across languages (e.g. model name + year), not just the model name.
/// </summary>
public static class LanguagePreference
{
    public static IReadOnlyList<T> PreferGermanThenEnglish<T>(
        IEnumerable<T> items, Func<T, string> modelKey, Func<T, string?> languageCode)
    {
        var result = new List<T>();
        foreach (var group in items.GroupBy(modelKey, StringComparer.OrdinalIgnoreCase))
        {
            var german = group.Where(i => Is(languageCode(i), "DE")).ToList();
            result.AddRange(german.Count > 0 ? german : group.Where(i => Is(languageCode(i), "EN")));
        }

        return result;
    }

    private static bool Is(string? languageCode, string expected) =>
        string.Equals(languageCode, expected, StringComparison.OrdinalIgnoreCase);
}
