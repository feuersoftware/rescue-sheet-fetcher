using System.Text.RegularExpressions;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>Small text helpers every label/page parser needs, in one place instead of a private copy
/// per parser.</summary>
internal static class LabelText
{
    /// <summary>Collapses every run of whitespace - including non-breaking spaces and line breaks - to a
    /// single space and trims the result.</summary>
    public static string Collapse(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A case-insensitive pattern matching any of <paramref name="alternatives"/> (a regex
    /// alternation such as <c>"SW|Break|Tourer"</c>) as a whole word: no letter or digit on either side.
    /// For a plain word list use <see cref="VehicleAttributeTextHelper.ExtractLastWordMatch"/>.</summary>
    public static Regex WholeWord(string alternatives) =>
        new($@"(?<![\p{{L}}\p{{N}}])(?:{alternatives})(?![\p{{L}}\p{{N}}])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The value of the first entry whose pattern matches <paramref name="text"/>, or null -
    /// for ordered (pattern, normalized value) tables where an earlier entry takes precedence.</summary>
    public static string? FirstMatch((Regex Pattern, string Value)[] table, string text) =>
        table.FirstOrDefault(entry => entry.Pattern.IsMatch(text)).Value;
}
