using System.Text.Json;
using Rettungskarten.Cli.Commands;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Storage;

namespace Rettungskarten.Tests.Cli;

public sealed class PrioritizeCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "prioritize-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task BrandManifest_CarriesTheNewPriorities()
    {
        // Regression: prioritize updated only the sidecars, so _manifest.json kept the old priorities
        // until the brand was fetched again.
        var cardsPath = Path.Combine(_root, "rescue-cards");
        var stockPath = Path.Combine(_root, "stock");
        var store = new FileSystemRescueCardStore(new RescueCardStoreOptions { RootPath = cardsPath });
        await store.SaveAsync(new RescueCardMetadata(
            Id: "vw-golf-1", Brand: Brand.VW, ModelName: "Golf", Variant: null,
            BodyType: null, BuildYearFrom: null, BuildYearTo: null, Doors: null, FuelType: null, LanguageCode: "DE",
            Status: RescueCardStatus.MetadataOnly, SourcePageUrl: "https://example.test/", DownloadUrl: "https://example.test/golf.pdf",
            FailureReason: null, ParseConfidence: ParseConfidence.Heuristic, DiscoveredAtUtc: DateTimeOffset.UtcNow,
            DownloadedAtUtc: null, LocalPdfRelativePath: null, SiblingModelIds: [], EstimatedFleetSize: null,
            BundlePriority: BundlePriority.Unknown), null, CancellationToken.None);
        await store.WriteBrandManifestAsync(Brand.VW, CancellationToken.None);
        await new FileSystemVehicleStockStore(new VehicleStockStoreOptions { RootPath = stockPath }).SaveAsync(
            new VehicleStockResult(Year: 2026, ReferenceDate: new DateOnly(2026, 1, 1),
                Rows: [new VehicleStockRow("Kompaktklasse", "VW", "GOLF", 3_231_990)],
                UnparsedRowWarnings: [], SourceUrl: "https://example.test/fz12.xlsx", License: "dl-de/by-2-0"),
            "fake-xlsx-bytes"u8.ToArray(), "fz12_2026.xlsx", CancellationToken.None);

        var exitCode = await PrioritizeCommand.Build()
            .Parse(["--rescue-cards-path", cardsPath, "--stock-path", stockPath]).InvokeAsync();

        Assert.Equal(0, exitCode);
        await using var manifest = File.OpenRead(Path.Combine(cardsPath, "vw", "_manifest.json"));
        var card = Assert.Single((await JsonSerializer.DeserializeAsync<List<RescueCardMetadata>>(manifest, JsonDefaults.Options))!);
        Assert.Equal(3_231_990, card.EstimatedFleetSize);
        Assert.NotEqual(BundlePriority.Unknown, card.BundlePriority);
    }
}
