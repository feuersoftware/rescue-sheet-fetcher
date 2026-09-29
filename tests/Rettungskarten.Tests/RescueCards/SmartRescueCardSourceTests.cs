using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Smart;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixtures are real: smart_index.html is rescuecard.smart.com's complete index page (an empty
/// Angular shell), smart_main_bundle.js the slice of its compiled main bundle that holds the model
/// catalogue (model keys, filename prefixes, language lists), with some of the surrounding pdf.js code.
/// </summary>
public sealed class SmartRescueCardSourceTests : IDisposable
{
    private const string BundleUrl = "https://rescuecard.smart.com/main.ec5f06cd01fd3c13.js";

    public SmartRescueCardSourceTests() => Strings.OverrideCulture = new CultureInfo("en");

    public void Dispose() => Strings.OverrideCulture = null;

    private static StubHttpClientFactory Factory(string? bundle = null) =>
        new StubHttpClientFactory()
            .Html(SmartRescueCardSource.SiteUrl, File.ReadAllText(Path.Combine("Fixtures", "smart_index.html")))
            .Respond(BundleUrl, _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(bundle ?? File.ReadAllText(Path.Combine("Fixtures", "smart_main_bundle.js")))
            });

    private static SmartRescueCardSource Source(StubHttpClientFactory factory) =>
        new(factory, NullLogger<SmartRescueCardSource>.Instance);

    [Fact]
    public async Task DiscoverAsync_ReadsEveryModelFromTheBundle()
    {
        var entries = await Source(Factory()).DiscoverAsync(CancellationToken.None);

        Assert.Equal(["#1", "#3", "#5"], entries.Select(e => e.Parsed.ModelName));

        var one = entries[0];
        // The literal '#' must travel as %23, or the server only sees ".../smart_".
        Assert.Equal("https://rescuecard.smart.com/assets/pdfs/1/smart_%231__SUV_2022_5d_Electric_DE.pdf", one.DownloadUrl);
        Assert.Equal("smart_#1__SUV_2022_5d_Electric_DE.pdf", one.RawFileNameOrLabel);
        Assert.Equal("https://rescuecard.smart.com/1", one.SourcePageUrl);
        Assert.Equal("SUV", one.Parsed.BodyType);
        Assert.Equal(2022, one.Parsed.BuildYearFrom);
        Assert.Equal(5, one.Parsed.Doors);
        Assert.Equal("Electric", one.Parsed.FuelType);
        Assert.Equal("DE", one.Parsed.LanguageCode);
        Assert.Equal(ManufacturerGroup.Geely, one.ManufacturerGroup);

        Assert.Equal("Hatchback", entries[1].Parsed.BodyType);
    }

    [Fact]
    public async Task DownloadAsync_SendsTheEscapedHash()
    {
        var factory = Factory().Bytes("https://rescuecard.smart.com/assets/pdfs/1/smart_%231__SUV_2022_5d_Electric_DE.pdf", StubHttpClientFactory.FakePdf());
        var source = Source(factory);
        var entry = (await source.DiscoverAsync(CancellationToken.None))[0];

        var result = await source.DownloadAsync(entry, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("%231", factory.Requests.Last().Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task DiscoverAsync_ModelWithoutGermanSheet_FallsBackToEnglish_OtherwiseSkipped()
    {
        const string bundle = """
            x={1:"smart_#1__SUV_2022_5d_Electric_",7:"smart_#7__SUV_2027_5d_Electric_",9:"smart_#9__SUV_2028_5d_Electric_"},
            y={1:["EN","DE"],7:["EN","FR"],9:["FR"]};
            """;

        var entries = await Source(Factory(bundle)).DiscoverAsync(CancellationToken.None);

        Assert.Equal(["DE", "EN"], entries.Select(e => e.Parsed.LanguageCode));
        Assert.EndsWith("_EN.pdf", entries[1].DownloadUrl);
    }

    [Fact]
    public async Task DiscoverAsync_BundleWithoutModelList_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Source(Factory("console.log('rebuilt');")).DiscoverAsync(CancellationToken.None));
    }

    [Fact]
    public void FindMainBundlePath_ReadsTheHashedFileName()
    {
        var html = File.ReadAllText(Path.Combine("Fixtures", "smart_index.html"));

        Assert.Equal("main.ec5f06cd01fd3c13.js", SmartAppBundleParser.FindMainBundlePath(html));
    }
}
