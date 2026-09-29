namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// <see cref="VehicleAttributeTextHelper.ExtractLastWordMatch"/> returns the matched text as it was
/// written - "suv" from a lower-case filename, "HYBRID" from an all-caps label, "Pick-Up" for the
/// vocabulary's "Pick-up". The brand parsers that read attributes from such text use this instead,
/// so the same attribute is always stored with the vocabulary's own spelling.
/// </summary>
internal static class CanonicalWordMatch
{
    public static string? Find(string text, IReadOnlyList<string> vocabulary)
    {
        var match = VehicleAttributeTextHelper.ExtractLastWordMatch(text, vocabulary);
        return match is null
            ? null
            : vocabulary.FirstOrDefault(v => v.Equals(match, StringComparison.OrdinalIgnoreCase)) ?? match;
    }
}
