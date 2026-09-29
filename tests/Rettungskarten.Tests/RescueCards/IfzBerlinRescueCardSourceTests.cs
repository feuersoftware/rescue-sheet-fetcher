using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.IfzBerlin;
using Rettungskarten.Tests.TestSupport;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>
/// Fixtures are real IFZ Berlin API responses (2026-09-29): the Opel/Vauxhall model-group list trimmed
/// to five groups, and those groups' German (land_id 1) and English (land_id 2) sheet lists as
/// returned - including the English "Zafira A (1999)" record that points at the Zafira Life file, and
/// a German label ending in a stray "\r\n".
///
/// No assertion here reads localized text (only the log lines go through Strings, into a NullLogger),
/// so these tests deliberately leave the process-global Strings.OverrideCulture alone - setting it
/// would race with the culture-asserting tests that run in parallel.
/// </summary>
public sealed class IfzBerlinRescueCardSourceTests
{
    private const string Api = "https://ifz-berlin.de/proxy/";
    private static readonly string[] Groups = ["Astra_L", "Zafira_A", "Zafira_D", "Movano_e", "Grandland"];

    [Fact]
    public async Task DiscoverAsync_Opel_ReadsGermanCollectionWithReferer()
    {
        var factory = Factory(landId: 1);

        var entries = await Source(Brand.Opel, factory).DiscoverAsync(CancellationToken.None);

        Assert.Equal(6 + 2 + 3 + 1 + 6, entries.Count);
        Assert.All(entries, e => Assert.Equal("DE", e.Parsed.LanguageCode));
        Assert.All(factory.Requests, r => Assert.Equal("https://www.ifz-berlin.de/index.html", r.Request.Headers.Referrer?.AbsoluteUri));

        var astra = Assert.Single(entries, e => e.RawFileNameOrLabel == "ret_deu_pdf/deu_ov_astra_l_hyb_wagon");
        Assert.Equal("https://www.ifz-berlin.de/ret_deu_pdf/deu_ov_astra_l_hyb_wagon.pdf", astra.DownloadUrl);
        Assert.Equal("Astra", astra.Parsed.ModelName);
        Assert.Equal("Kombi", astra.Parsed.BodyType);
        Assert.Equal("Hybrid", astra.Parsed.FuelType);
        Assert.Equal(2021, astra.Parsed.BuildYearFrom);

        var grandland = Assert.Single(entries, e => e.RawFileNameOrLabel == "ret_deu_pdf/deu_ov_grandland_suv_2024");
        Assert.Equal("Grandland SUV (2024)", grandland.Parsed.Variant);

        Assert.All(entries.Where(e => e.RawFileNameOrLabel.Contains("zafira_d", StringComparison.Ordinal) || e.RawFileNameOrLabel.EndsWith("zafira_life", StringComparison.Ordinal)),
            e => Assert.Equal("Zafira Life", e.Parsed.ModelName));
        Assert.Equal("Elektro", Assert.Single(entries, e => e.Parsed.ModelName == "Movano").Parsed.FuelType);
    }

    [Fact]
    public async Task DiscoverAsync_Vauxhall_ReadsEnglishCollectionAndResolvesSharedFile()
    {
        var factory = Factory(landId: 2);

        var entries = await Source(Brand.Vauxhall, factory).DiscoverAsync(CancellationToken.None);

        Assert.All(entries, e => Assert.Equal(Brand.Vauxhall, e.Brand));
        Assert.All(entries, e => Assert.Equal("EN", e.Parsed.LanguageCode));
        Assert.Contains(factory.Requests, r => r.Request.RequestUri!.Query.Contains("land_id=2", StringComparison.Ordinal));

        // "Zafira A (1999)" and "Zafira Life (2019)" both link eng_opelvauxhall_zafira_life: one entry,
        // labelled as the record the filename actually names.
        var life = Assert.Single(entries, e => e.RawFileNameOrLabel == "ret_eng_pdf/eng_opelvauxhall_zafira_life");
        Assert.Equal("Zafira Life", life.Parsed.ModelName);
        Assert.Equal(2019, life.Parsed.BuildYearFrom);
        Assert.Equal(entries.Count, entries.Select(e => e.RawFileNameOrLabel).Distinct().Count());
    }

    [Fact]
    public async Task DiscoverAsync_OneGroupFails_KeepsTheOthers()
    {
        var factory = Factory(landId: 1)
            .Status($"{Api}index_ret_8_4.php?land_id=1&fabrikat_nr=1&auto_typ=Astra_L", HttpStatusCode.InternalServerError);

        var entries = await Source(Brand.Opel, factory).DiscoverAsync(CancellationToken.None);

        Assert.Equal(2 + 3 + 1 + 6, entries.Count);
        Assert.DoesNotContain(entries, e => e.Parsed.ModelName == "Astra");
    }

    [Fact]
    public async Task DiscoverAsync_ModelGroupListFails_Throws()
    {
        var factory = new StubHttpClientFactory().Status($"{Api}index_ret_8_3.php?fabrikat_nr=99", HttpStatusCode.Forbidden);

        await Assert.ThrowsAsync<HttpRequestException>(() => Source(Brand.Saab, factory).DiscoverAsync(CancellationToken.None));
    }

    private static IfzBerlinRescueCardSource Source(Brand brand, StubHttpClientFactory factory) =>
        new(brand, factory, new DiscoveryResponseCache(), NullLogger<IfzBerlinRescueCardSource>.Instance);

    private static StubHttpClientFactory Factory(int landId)
    {
        var factory = new StubHttpClientFactory().Json($"{Api}index_ret_8_3.php?fabrikat_nr=1", Fixture("ifz_autotypen_1.json"));
        foreach (var group in Groups)
        {
            factory.Json($"{Api}index_ret_8_4.php?land_id={landId}&fabrikat_nr=1&auto_typ={group}", Fixture($"ifz_sheets_{landId}_1_{group}.json"));
        }

        return factory;
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine("Fixtures", name));
}
