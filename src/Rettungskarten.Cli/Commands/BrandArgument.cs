using Rettungskarten.Core.Models;

namespace Rettungskarten.Cli.Commands;

/// <summary>
/// The <c>--brand</c>/<c>split &lt;brand&gt;</c> values: every <see cref="Brand"/> name lower-cased
/// ("vw", "mercedesbenz", "landrover", ...) plus "all". Derived from the enum rather than listed by
/// hand, so a new brand is a valid argument - and part of `--brand all`, and therefore of the weekly
/// link check - the moment its enum value exists.
/// </summary>
public static class BrandArgument
{
    public const string All = "all";

    public static string[] AllowedValues() =>
        [.. Enum.GetValues<Brand>().Select(ToArgument), All];

    public static string ToArgument(Brand brand) => brand.ToString().ToLowerInvariant();

    /// <summary>Every brand for "all", otherwise the one named brand (case-insensitive).</summary>
    public static IReadOnlyList<Brand> Resolve(string value) =>
        value.Equals(All, StringComparison.OrdinalIgnoreCase)
            ? Enum.GetValues<Brand>()
            : [Enum.Parse<Brand>(value, ignoreCase: true)];
}
