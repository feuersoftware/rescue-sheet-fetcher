using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Rettungskarten.Cli;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Tests.Cli;

/// <summary>
/// Portal sources are registered through factory lambdas (one instance per brand), which the
/// container only resolves at run time - a constructor-signature mismatch or a forgotten registration
/// would otherwise first show up as a failed `fetch` or a silent "not implemented" brand.
/// </summary>
public class CompositionRootTests : IDisposable
{
    public void Dispose() => Strings.OverrideCulture = null;

    [Fact]
    public void EveryBrand_HasAtLeastOneResolvableSource()
    {
        using var services = CompositionRoot.Build(verbose: false);

        var sources = services.GetServices<IRescueCardSource>().ToList();

        var missing = Enum.GetValues<Brand>().Where(b => sources.All(s => s.Brand != b)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryBrandSource_ResolvesToExactlyOneInstancePerBrand_ExceptSmart()
    {
        using var services = CompositionRoot.Build(verbose: false);

        var perBrand = services.GetServices<IRescueCardSource>().GroupBy(s => s.Brand).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(2, perBrand[Brand.Smart]); // Mercedes portal (<= 2021) + smart's own site (>= 2022)
        Assert.All(perBrand.Where(p => p.Key != Brand.Smart), p => Assert.Equal(1, p.Value));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EveryBrand_HasASourceDescriptionForListBrands(string culture)
    {
        Strings.OverrideCulture = new CultureInfo(culture);

        var missing = Enum.GetValues<Brand>().Where(b => Strings.TryGet($"Brand_{b}_Source") is null).ToList();

        Assert.Empty(missing);
    }
}
