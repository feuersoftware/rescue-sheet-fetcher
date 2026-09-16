using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Storage;

namespace Rettungskarten.Tests.Storage;

public class FileSystemVehicleStockStoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "rettungskarten-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static VehicleStockResult BuildResult(int year) => new(
        Year: year, ReferenceDate: new DateOnly(year, 1, 1),
        Rows: [new VehicleStockRow("Kompaktklasse", "VW", "Golf", 3_231_990)],
        UnparsedRowWarnings: [], SourceUrl: "https://example.test/fz12.xlsx", License: "dl-de/by-2-0");

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var store = new FileSystemVehicleStockStore(new VehicleStockStoreOptions { RootPath = _tempRoot });
        var result = BuildResult(2026);

        await store.SaveAsync(result, "fake-xlsx-bytes"u8.ToArray(), "fz12_2026.xlsx", CancellationToken.None);
        var loaded = await store.LoadAsync(2026, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(2026, loaded.Year);
        Assert.Single(loaded.Rows);
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTempFileBehind()
    {
        var store = new FileSystemVehicleStockStore(new VehicleStockStoreOptions { RootPath = _tempRoot });

        await store.SaveAsync(BuildResult(2026), "fake-xlsx-bytes"u8.ToArray(), "fz12_2026.xlsx", CancellationToken.None);

        Assert.Empty(Directory.EnumerateFiles(_tempRoot, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task LoadAsync_CorruptResultFile_ReturnsNullInsteadOfThrowing()
    {
        // Regression test: a truncated/corrupt fz12_{year}.json must not bubble a raw JsonException
        // up to the command level - the caller already treats a null result as "no stock data found".
        var store = new FileSystemVehicleStockStore(new VehicleStockStoreOptions { RootPath = _tempRoot });
        await store.SaveAsync(BuildResult(2026), "fake-xlsx-bytes"u8.ToArray(), "fz12_2026.xlsx", CancellationToken.None);

        var resultPath = Path.Combine(_tempRoot, "2026", "fz12_2026.json");
        await File.WriteAllTextAsync(resultPath, "{ not valid json");

        var loaded = await store.LoadAsync(2026, CancellationToken.None);

        Assert.Null(loaded);
    }
}
