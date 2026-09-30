using Rettungskarten.Cli.Commands;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Tests.Cli;

public class BrandArgumentTests
{
    [Fact]
    public void AllowedValues_AreEveryLowerCasedBrandPlusAll()
    {
        var values = BrandArgument.AllowedValues();

        Assert.Equal(Enum.GetValues<Brand>().Length + 1, values.Length);
        Assert.Contains("mercedesbenz", values);
        Assert.Contains(BrandArgument.All, values);
    }

    [Fact]
    public void Resolve_AllOrOneBrand_CaseInsensitive()
    {
        Assert.Equal(Enum.GetValues<Brand>(), BrandArgument.Resolve("ALL"));
        Assert.Equal([Brand.AlfaRomeo], BrandArgument.Resolve("alfaromeo"));
        Assert.Equal(Brand.Tesla, BrandArgument.Parse("Tesla"));
    }
}
