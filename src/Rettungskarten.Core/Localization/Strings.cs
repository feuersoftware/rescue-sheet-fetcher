using System.Globalization;
using System.Resources;

namespace Rettungskarten.Core.Localization;

/// <summary>
/// Thin wrapper around the app's resource strings (Strings.resx = English/neutral,
/// Strings.de.resx = German satellite). Lives in Core so both Infrastructure (exception/log
/// messages) and Cli (command descriptions, console output) can share one localized vocabulary.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager ResourceManager =
        new("Rettungskarten.Core.Localization.Strings", typeof(Strings).Assembly);

    /// <summary>
    /// Set once at startup from the resolved --lang option (or left null to follow
    /// <see cref="CultureInfo.CurrentUICulture"/>, i.e. the OS/environment language).
    /// </summary>
    public static CultureInfo? OverrideCulture { get; set; }

    private static CultureInfo EffectiveCulture => OverrideCulture ?? CultureInfo.CurrentUICulture;

    public static string Get(string name) =>
        ResourceManager.GetString(name, EffectiveCulture) ?? name;

    /// <summary>Like <see cref="Get(string)"/>, but returns null instead of the key itself when the
    /// resource doesn't exist - for optional per-brand entries (e.g. a brand-specific status text
    /// that falls back to a generic one).</summary>
    public static string? TryGet(string name) => ResourceManager.GetString(name, EffectiveCulture);

    public static string Get(string name, params object[] args)
    {
        var format = Get(name);
        return args.Length == 0 ? format : string.Format(EffectiveCulture, format, args);
    }

    /// <summary>Resolves a "de"/"en" CLI argument to a CultureInfo, or null for anything else
    /// (including no value), which leaves <see cref="CultureInfo.CurrentUICulture"/> in charge.</summary>
    public static CultureInfo? ParseLanguageOption(string? value) => value?.ToLowerInvariant() switch
    {
        "de" => new CultureInfo("de"),
        "en" => new CultureInfo("en"),
        _ => null
    };
}
