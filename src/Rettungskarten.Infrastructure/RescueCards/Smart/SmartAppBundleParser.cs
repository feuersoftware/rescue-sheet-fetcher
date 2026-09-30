using System.Text.RegularExpressions;

namespace Rettungskarten.Infrastructure.RescueCards.Smart;

/// <summary>One model as smart's rescue-card app defines it.</summary>
/// <param name="Key">The app's model key and route ("1" for smart #1), also the PDF folder name.</param>
/// <param name="FileNamePrefix">The PDF filename without its language suffix, e.g.
/// "smart_#1__SUV_2022_5d_Electric_" (the app appends "DE.pdf", "EN.pdf", ...).</param>
/// <param name="Languages">The language codes the app offers for this model; empty when the list
/// couldn't be read.</param>
public sealed record SmartAppModel(string Key, string FileNamePrefix, IReadOnlyList<string> Languages);

/// <summary>
/// Reads the model list out of rescuecard.smart.com's compiled Angular bundle - the site has no API;
/// the whole catalogue is two object literals in <c>main.&lt;hash&gt;.js</c>:
/// <c>{1:"smart_#1__SUV_2022_5d_Electric_",3:"smart_#3__Hatchback_2023_5d_Electric_",...}</c>
/// (model key -> filename prefix) and <c>{1:["EN","DE","FR",...],3:[...],...}</c> (model key ->
/// languages). The minified variable names change with every build, so both are matched by the shape
/// of their content instead: a numeric key followed by a string starting with "smart_", and a numeric
/// key followed by an array of two-letter upper-case codes. Reading the bundle rather than hard-coding
/// the three models means a new one (#5 was added in 2025) is picked up without a code change.
/// </summary>
public static class SmartAppBundleParser
{
    private static readonly Regex MainBundlePattern = new(
        @"<script[^>]+src=""(?<src>main[^""]*\.js)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FileNamePrefixPattern = new(
        @"(?<![\w$])(?<key>\d+):""(?<prefix>smart_[^""\\]+)""", RegexOptions.Compiled);

    private static readonly Regex LanguageListPattern = new(
        @"(?<![\w$])(?<key>\d+):\[(?<langs>""[A-Z]{2}""(?:,""[A-Z]{2}"")*)\]", RegexOptions.Compiled);

    /// <summary>The main bundle's path as referenced by the app's index page, or null.</summary>
    public static string? FindMainBundlePath(string indexHtml)
    {
        var match = MainBundlePattern.Match(indexHtml);
        return match.Success ? match.Groups["src"].Value : null;
    }

    public static IReadOnlyList<SmartAppModel> ParseModels(string bundleJs)
    {
        var languages = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (Match match in LanguageListPattern.Matches(bundleJs))
        {
            languages.TryAdd(match.Groups["key"].Value,
                match.Groups["langs"].Value.Split(',').Select(l => l.Trim('"')).ToList());
        }

        var models = new List<SmartAppModel>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in FileNamePrefixPattern.Matches(bundleJs))
        {
            var key = match.Groups["key"].Value;
            if (seenKeys.Add(key))
            {
                models.Add(new SmartAppModel(key, match.Groups["prefix"].Value, languages.GetValueOrDefault(key) ?? []));
            }
        }

        return models;
    }
}
