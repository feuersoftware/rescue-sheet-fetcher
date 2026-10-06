using Microsoft.Extensions.Logging.Abstractions;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Config;
using Rettungskarten.Core.Models;
using Rettungskarten.Core.Orchestration;

namespace Rettungskarten.Tests.Orchestration;

public class RescueCardOrchestratorTests
{
    [Fact]
    public async Task SeveralSourcesForOneBrand_AreAllDiscovered_AndEachEntryDownloadsThroughItsOwnSource()
    {
        // smart: Mercedes' portal for the models up to 2021, smart's own site from 2022 (Geely JV).
        var legacy = new FakeSource(Brand.Smart, Entry("fortwo", null));
        var current = new FakeSource(Brand.Smart, Entry("#1", ManufacturerGroup.Geely));
        var store = new RecordingStore();
        var orchestrator = new RescueCardOrchestrator([legacy, current], store, SiblingModelsConfig.Empty, NullLogger<RescueCardOrchestrator>.Instance);

        var result = await orchestrator.RunForBrandAsync(Brand.Smart, dryRun: false, CancellationToken.None);

        Assert.Equal(BrandRunOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.Downloaded);
        Assert.Equal(["fortwo"], legacy.Downloaded);
        Assert.Equal(["#1"], current.Downloaded);
        Assert.Equal(ManufacturerGroup.MercedesBenzGroup, store.Saved.Single(m => m.ModelName == "fortwo").ManufacturerGroup);
        Assert.Equal(ManufacturerGroup.Geely, store.Saved.Single(m => m.ModelName == "#1").ManufacturerGroup);
    }

    [Fact]
    public async Task OneOfSeveralSourcesFailing_KeepsTheOthersCards_ButReportsDiscoveryFailed()
    {
        var working = new FakeSource(Brand.Smart, Entry("fortwo", null));
        var broken = new FakeSource(Brand.Smart) { DiscoveryError = new HttpRequestException("moved") };
        var store = new RecordingStore();
        var orchestrator = new RescueCardOrchestrator([working, broken], store, SiblingModelsConfig.Empty, NullLogger<RescueCardOrchestrator>.Instance);

        var result = await orchestrator.RunForBrandAsync(Brand.Smart, dryRun: true, CancellationToken.None);

        Assert.Equal(BrandRunOutcome.DiscoveryFailed, result.Outcome);
        Assert.Equal(1, result.Discovered);
        Assert.Contains("moved", result.Note);
        Assert.Single(store.Saved);
    }

    [Fact]
    public async Task EntryScopeAndChassisCode_ArePersisted()
    {
        var parsed = new ParsedModelInfo("All Models", null, null, null, null, null, null, "DE", ParseConfidence.Heuristic, ChassisCode: "W177");
        var source = new FakeSource(Brand.Ford, new RescueCardEntry(Brand.Ford, "https://page.test", "https://page.test/all.pdf", "all.pdf", parsed, DocumentScope.Combined));
        var store = new RecordingStore();
        var orchestrator = new RescueCardOrchestrator([source], store, SiblingModelsConfig.Empty, NullLogger<RescueCardOrchestrator>.Instance);

        await orchestrator.RunForBrandAsync(Brand.Ford, dryRun: true, CancellationToken.None);

        var saved = Assert.Single(store.Saved);
        Assert.Equal(DocumentScope.Combined, saved.DocumentScope);
        Assert.Equal("W177", saved.ChassisCode);
        Assert.Equal(ManufacturerGroup.Independent, saved.ManufacturerGroup);
    }

    [Fact]
    public async Task StoreFailing_IsReportedAsAFailedBrand_NotThrown()
    {
        // Regression: brands run in parallel via Task.WhenAll; an exception from the store escaped
        // RunForBrandAsync and discarded every other brand's result and the run summary.
        var source = new FakeSource(Brand.Smart, Entry("fortwo", null), Entry("forfour", null));
        var store = new RecordingStore { FailAfter = 1 };
        var orchestrator = new RescueCardOrchestrator([source], store, SiblingModelsConfig.Empty, NullLogger<RescueCardOrchestrator>.Instance);

        var result = await orchestrator.RunForBrandAsync(Brand.Smart, dryRun: false, CancellationToken.None);

        Assert.Equal(BrandRunOutcome.DiscoveryFailed, result.Outcome);
        Assert.Contains("disk full", result.Note);
        Assert.Equal(1, result.Discovered); // the card saved before the failure is still reported
    }

    [Theory]
    [InlineData("PHEV", "Plug-in Hybrid")]
    [InlineData("Plug-in-Hybrid", "Plug-in Hybrid")]
    [InlineData("Elektro", "Electric")]
    [InlineData("BEV", "Electric")]
    [InlineData("GD", "Petrol/Diesel")]
    [InlineData("Hybride", "Hybrid")]
    [InlineData("Wasserstoff", "Hydrogen")]
    [InlineData("FHybrid", "Hybrid")] // Ford
    [InlineData("Energi", "Plug-in Hybrid")] // Ford C-MAX/Fusion Energi
    [InlineData("Hybrid Benzin", "Hybrid Benzin")] // unknown combination: kept, not guessed
    public async Task FuelType_IsStoredInTheSharedVocabulary(string parsedFuel, string storedFuel)
    {
        var parsed = new ParsedModelInfo("X", null, null, null, null, null, parsedFuel, "DE", ParseConfidence.Heuristic);
        var source = new FakeSource(Brand.BMW, new RescueCardEntry(Brand.BMW, "https://page.test", "https://page.test/x.pdf", "x.pdf", parsed));
        var store = new RecordingStore();
        var orchestrator = new RescueCardOrchestrator([source], store, SiblingModelsConfig.Empty, NullLogger<RescueCardOrchestrator>.Instance);

        await orchestrator.RunForBrandAsync(Brand.BMW, dryRun: true, CancellationToken.None);

        Assert.Equal(storedFuel, Assert.Single(store.Saved).FuelType);
    }

    private static RescueCardEntry Entry(string model, ManufacturerGroup? group) => new(
        Brand.Smart, "https://page.test", $"https://page.test/{Uri.EscapeDataString(model)}.pdf", model,
        new ParsedModelInfo(model, null, null, null, null, null, null, "DE", ParseConfidence.Heuristic),
        ManufacturerGroupOverride: group);

    private sealed class FakeSource(Brand brand, params RescueCardEntry[] entries) : IRescueCardSource
    {
        public Brand Brand => brand;

        public Exception? DiscoveryError { get; init; }

        public List<string> Downloaded { get; } = [];

        public Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct) =>
            DiscoveryError is null ? Task.FromResult<IReadOnlyList<RescueCardEntry>>(entries) : Task.FromException<IReadOnlyList<RescueCardEntry>>(DiscoveryError);

        public Task<RescueCardDownloadResult> DownloadAsync(RescueCardEntry entry, CancellationToken ct)
        {
            Downloaded.Add(entry.RawFileNameOrLabel);
            return Task.FromResult(RescueCardDownloadResult.Ok("%PDF-"u8.ToArray()));
        }
    }

    private sealed class RecordingStore : IRescueCardFileStore
    {
        public List<RescueCardMetadata> Saved { get; } = [];

        public int? FailAfter { get; init; }

        public Task<RescueCardMetadata> SaveAsync(RescueCardMetadata metadata, byte[]? pdfContentOrNull, CancellationToken ct)
        {
            if (Saved.Count == FailAfter)
            {
                throw new IOException("disk full");
            }

            Saved.Add(metadata);
            return Task.FromResult(metadata);
        }

        public Task WriteBrandManifestAsync(Brand brand, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<RescueCardMetadata>> LoadAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<RescueCardMetadata>>(Saved);

        public Task UpdateMetadataAsync(RescueCardMetadata metadata, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteAsync(RescueCardMetadata metadata, CancellationToken ct) => Task.CompletedTask;

        public string? GetPdfPath(RescueCardMetadata metadata) => null;
    }
}
