using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Storage;

namespace Rettungskarten.Tests.Storage;

public class FileSystemRescueCardStoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "rettungskarten-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static RescueCardMetadata BuildMetadata(string id, string modelName) => new(
        Id: id, Brand: Brand.Porsche, ModelName: modelName, Variant: null, BodyType: null,
        BuildYearFrom: null, BuildYearTo: null, Doors: null, FuelType: null, LanguageCode: "EN",
        Status: RescueCardStatus.Downloaded, SourcePageUrl: "https://example.test", DownloadUrl: "https://example.test/a.pdf",
        FailureReason: null, ParseConfidence: ParseConfidence.Heuristic,
        DiscoveredAtUtc: DateTimeOffset.UtcNow, DownloadedAtUtc: DateTimeOffset.UtcNow,
        LocalPdfRelativePath: null, SiblingModelIds: [], EstimatedFleetSize: null, BundlePriority: BundlePriority.Unknown);

    [Fact]
    public async Task DeleteAsync_LastEntryInModelFolder_RemovesNowEmptyFolder()
    {
        var store = new FileSystemRescueCardStore(new RescueCardStoreOptions { RootPath = _tempRoot });
        var metadata = BuildMetadata("porsche-all-models-abc123", "All Models");

        var saved = await store.SaveAsync(metadata, "%PDF-1.4 fake"u8.ToArray(), CancellationToken.None);
        var modelFolder = Path.Combine(_tempRoot, "porsche", "all-models");
        Assert.True(Directory.Exists(modelFolder));

        await store.DeleteAsync(saved, CancellationToken.None);

        Assert.False(Directory.Exists(modelFolder));
    }

    [Fact]
    public async Task DeleteAsync_OtherEntriesRemainInModelFolder_KeepsFolder()
    {
        var store = new FileSystemRescueCardStore(new RescueCardStoreOptions { RootPath = _tempRoot });
        var first = await store.SaveAsync(BuildMetadata("porsche-911-a", "911"), "%PDF-1.4 fake"u8.ToArray(), CancellationToken.None);
        var second = await store.SaveAsync(BuildMetadata("porsche-911-b", "911"), "%PDF-1.4 fake"u8.ToArray(), CancellationToken.None);

        await store.DeleteAsync(first, CancellationToken.None);

        var modelFolder = Path.Combine(_tempRoot, "porsche", "911");
        Assert.True(Directory.Exists(modelFolder));
        Assert.True(File.Exists(Path.Combine(modelFolder, "porsche-911-b.json")));
    }

    [Fact]
    public async Task LoadAllAsync_OneCorruptFile_SkipsItAndLoadsTheRest()
    {
        // Regression test: a single truncated/corrupt sidecar (e.g. from a crash mid-write, or
        // external interference) must not abort loading every other card in the store.
        var store = new FileSystemRescueCardStore(new RescueCardStoreOptions { RootPath = _tempRoot });
        await store.SaveAsync(BuildMetadata("porsche-golf-1", "Golf"), null, CancellationToken.None);
        await store.SaveAsync(BuildMetadata("porsche-polo-1", "Polo"), null, CancellationToken.None);

        var corruptFile = Directory.EnumerateFiles(_tempRoot, "*.json", SearchOption.AllDirectories)
            .Single(f => Path.GetFileNameWithoutExtension(f) == "porsche-golf-1");
        await File.WriteAllTextAsync(corruptFile, "{ this is not valid json");

        var loaded = await store.LoadAllAsync(CancellationToken.None);

        var item = Assert.Single(loaded);
        Assert.Equal("porsche-polo-1", item.Id);
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTempFileBehind()
    {
        var store = new FileSystemRescueCardStore(new RescueCardStoreOptions { RootPath = _tempRoot });

        await store.SaveAsync(BuildMetadata("porsche-911-c", "911"), null, CancellationToken.None);

        Assert.Empty(Directory.EnumerateFiles(_tempRoot, "*.tmp", SearchOption.AllDirectories));
    }
}
