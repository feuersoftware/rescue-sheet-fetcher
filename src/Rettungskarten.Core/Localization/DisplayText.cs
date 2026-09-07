using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Localization;

/// <summary>Localized display text for domain enums that get printed directly to the console.</summary>
public static class DisplayText
{
    public static string For(BundlePriority priority) => priority switch
    {
        BundlePriority.High => Strings.Get("Priority_High"),
        BundlePriority.Medium => Strings.Get("Priority_Medium"),
        BundlePriority.Low => Strings.Get("Priority_Low"),
        _ => Strings.Get("Priority_Unknown")
    };

    public static string For(BrandRunOutcome outcome) => outcome switch
    {
        BrandRunOutcome.Completed => Strings.Get("Outcome_Completed"),
        BrandRunOutcome.DiscoveryFailed => Strings.Get("Outcome_DiscoveryFailed"),
        BrandRunOutcome.NotImplemented => Strings.Get("Outcome_NotImplemented"),
        _ => outcome.ToString()
    };
}
