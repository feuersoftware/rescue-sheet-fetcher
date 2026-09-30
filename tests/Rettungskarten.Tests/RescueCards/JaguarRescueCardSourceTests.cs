using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>Fixture (jaguar_page.html): six real links from jaguar.com/de-de's rescue-card page,
/// including the real mislabelled pair (the "XK CABRIOLET (2005-2014)" link points at the Coupé's
/// file, which the "XK COUPÉ (2005-2014)" link right after it also points at), plus a non-rescue PDF
/// outside the rescue-sheet folder.</summary>
public sealed class JaguarRescueCardSourceTests
{
    private const string PageUrl = "https://www.jaguar.com/de-de/jdx/service-und-zubehor/service-garantien/rettungskarten.html";

    private static async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync()
    {
        var html = await File.ReadAllTextAsync(Path.Combine("Fixtures", "jaguar_page.html"));
        var factory = new StubHttpClientFactory().Html(PageUrl, html);
        return await new JaguarRescueCardSource(factory, NullLogger<JaguarRescueCardSource>.Instance).DiscoverAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DiscoverAsync_SkipsLinkWhoseLabelContradictsItsFile_KeepsCorrectlyLabelledOne()
    {
        var entries = await DiscoverAsync();

        Assert.Equal(5, entries.Count);
        var xk = Assert.Single(entries, e => e.Parsed.ModelName == "XK");
        Assert.Equal("Coupé", xk.Parsed.BodyType);
        Assert.EndsWith("XK_Coupe_2005-2014.pdf", xk.DownloadUrl);
        Assert.DoesNotContain(entries, e => e.Parsed.Variant!.Contains("brochure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoverAsync_ParsesLabelAndFilename()
    {
        var entries = await DiscoverAsync();

        var xe = Assert.Single(entries, e => e.Parsed.ModelName == "XE");
        Assert.Equal(2021, xe.Parsed.BuildYearFrom);
        Assert.Null(xe.Parsed.BuildYearTo);
        Assert.Equal("MHEV", xe.Parsed.FuelType); // only the filename says so

        var sportbrake = Assert.Single(entries, e => e.Parsed.ModelName == "XF");
        Assert.Equal("Sportbrake", sportbrake.Parsed.BodyType);
        Assert.Equal(2020, sportbrake.Parsed.BuildYearTo);

        var ipace = Assert.Single(entries, e => e.Parsed.ModelName == "I-PACE");
        Assert.Equal(2018, ipace.Parsed.BuildYearFrom); // label has no year; the filename does

        var ftype = Assert.Single(entries, e => e.Parsed.ModelName == "F-TYPE");
        Assert.Equal("Cabriolet", ftype.Parsed.BodyType);
    }
}
