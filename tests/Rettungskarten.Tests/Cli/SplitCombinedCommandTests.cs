using System.CommandLine;
using Rettungskarten.Cli.Commands;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Storage;

namespace Rettungskarten.Tests.Cli;

public sealed class SplitCombinedCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "split-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidCombinedDocument_ExitsZero()
    {
        await SaveCombinedKiaEntryAsync("kia-all-1", File.ReadAllBytes(Path.Combine("Fixtures", "kia_sample_pages.pdf")));

        Assert.Equal(0, await RunSplitAsync("kia"));
    }

    [Fact]
    public async Task CorruptedCombinedDocument_ExitsNonZero_EvenThoughOthersAreSplit()
    {
        // Regression: any combined entry found meant exit code 0, even when every document threw or
        // produced no parts - automation couldn't tell a broken split from a successful one.
        await SaveCombinedKiaEntryAsync("kia-all-1", File.ReadAllBytes(Path.Combine("Fixtures", "kia_sample_pages.pdf")));
        await SaveCombinedKiaEntryAsync("kia-all-2", "not a pdf at all"u8.ToArray());

        Assert.Equal(1, await RunSplitAsync("kia"));
    }

    [Fact]
    public async Task CombinedEntryWhosePdfIsMissing_ExitsNonZero()
    {
        await SaveCombinedKiaEntryAsync("kia-all-1", File.ReadAllBytes(Path.Combine("Fixtures", "kia_sample_pages.pdf")));
        foreach (var pdf in Directory.EnumerateFiles(_root, "*.pdf", SearchOption.AllDirectories))
        {
            File.Delete(pdf);
        }

        Assert.Equal(1, await RunSplitAsync("kia"));
    }

    [Theory]
    [InlineData(new[] { 5, 6, 7, 8 }, "5-8")]
    [InlineData(new[] { 3, 5, 6, 7, 12 }, "3, 5-7, 12")]
    [InlineData(new[] { 9 }, "9")]
    public void PageList_CompressesRuns(int[] pages, string expected) =>
        Assert.Equal(expected, SplitCombinedCommand.PageList(pages));

    private async Task<int> RunSplitAsync(string brand)
    {
        var split = new Command("split");
        SplitCombinedCommand.Configure(split);
        return await split.Parse([brand, "--rescue-cards-path", _root, "--sibling-config", Path.Combine(_root, "none.json")]).InvokeAsync();
    }

    private Task SaveCombinedKiaEntryAsync(string id, byte[] pdf)
    {
        var store = new FileSystemRescueCardStore(new RescueCardStoreOptions { RootPath = _root });
        var metadata = new RescueCardMetadata(
            Id: id, Brand: Brand.Kia, ModelName: "All Models", Variant: "Kia Rettungsdatenblätter für ältere Modelle",
            BodyType: null, BuildYearFrom: null, BuildYearTo: 2020, Doors: null, FuelType: null, LanguageCode: "DE",
            Status: RescueCardStatus.Downloaded, SourcePageUrl: "https://www.kia.com/de/", DownloadUrl: $"https://www.kia.com/{id}.pdf",
            FailureReason: null, ParseConfidence: ParseConfidence.Heuristic, DiscoveredAtUtc: DateTimeOffset.UtcNow,
            DownloadedAtUtc: DateTimeOffset.UtcNow, LocalPdfRelativePath: null, SiblingModelIds: [], EstimatedFleetSize: null,
            BundlePriority: BundlePriority.Unknown, DocumentScope: DocumentScope.Combined);
        return store.SaveAsync(metadata, pdf, CancellationToken.None);
    }
}
