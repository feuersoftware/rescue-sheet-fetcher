using System.Text.RegularExpressions;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

public enum RescueDocumentKind
{
    /// <summary>A real per-model rescue sheet ("Rettungsdatenblatt"/"Rettungskarte").</summary>
    RescueSheet,

    /// <summary>An Emergency Response Guide (ERG): a long, model-specific handbook for high-voltage and
    /// hydrogen vehicles that complements - but doesn't replace - the one-page rescue sheet. Several
    /// manufacturers (Stellantis Servicebox, Nissan, Tesla, Lamborghini) list them right next to the
    /// rescue sheets.</summary>
    EmergencyResponseGuide,

    /// <summary>Anything else found on the same pages: general guides, legends/pictogram
    /// explanations, owner's or towing manuals.</summary>
    OtherDocument
}

/// <summary>
/// Tells real rescue sheets apart from the other documents manufacturers publish on the same pages,
/// from the link label and/or filename. Only rescue sheets are collected (decision from the brand
/// expansion plan): an ERG is valuable but a different document type, and mixing it in would make
/// "one card per model variant" and every count built on it wrong.
///
/// The patterns are deliberately conservative - only unambiguous document-type words, never a model
/// or body-style word - and they are applied to the label and the filename *only*, never the page
/// URL (Hyundai's page itself is called "anleitungen-und-datenblaetter").
/// </summary>
public static class RescueDocumentClassifier
{
    private static readonly Regex ErgPattern = new(
        @"(?<![A-Za-z])ERG(?![A-Za-z])|emergency[\s_-]*response[\s_-]*guide|first[\s_-]*responder[\s_-]*guide|Einsatzleitfaden|Notfallleitfaden",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OtherDocumentPattern = new(
        @"Legende|(?<![A-Za-z])legend(?![A-Za-z])|Piktogramm|pictogram|Symbolerkl|Erläuterung|explanation|Leitfaden|" +
        @"Handbuch|(?<![A-Za-z])manual(?![A-Za-z])|Betriebsanleitung|Bedienungsanleitung|Owner'?s|Abschlepp|towing|" +
        @"Allgemeine[\s_-]*Informationen|general[\s_-]*information|Hinweise[\s_-]*zur[\s_-]*Nutzung|how[\s_-]*to[\s_-]*read",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static RescueDocumentKind Classify(string labelOrFileName)
    {
        if (ErgPattern.IsMatch(labelOrFileName))
        {
            return RescueDocumentKind.EmergencyResponseGuide;
        }

        return OtherDocumentPattern.IsMatch(labelOrFileName) ? RescueDocumentKind.OtherDocument : RescueDocumentKind.RescueSheet;
    }

    /// <summary>True when neither the label nor the filename marks the document as anything but a
    /// rescue sheet.</summary>
    public static bool IsRescueSheet(string? label, string? fileName) =>
        Classify($"{label} {fileName}") == RescueDocumentKind.RescueSheet;
}
