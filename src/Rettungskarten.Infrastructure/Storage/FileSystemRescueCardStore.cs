using System.Text.Json;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Core.Naming;

namespace Rettungskarten.Infrastructure.Storage;

public sealed record RescueCardStoreOptions
{
    public string RootPath { get; init; } = Path.Combine("data", "rescue-cards");
}

/// <summary>
/// Layout: {root}/{brand}/{modelSlug}/{id}.json (+ .pdf if downloaded), plus a derived
/// {root}/{brand}/_manifest.json aggregating every sidecar in that brand's folder. The per-card
/// sidecars are the source of truth; the manifest is regenerated on every run, never hand-edited.
/// </summary>
public sealed class FileSystemRescueCardStore(RescueCardStoreOptions options) : IRescueCardFileStore
{
    private const string ManifestFileName = "_manifest.json";

    public async Task<RescueCardMetadata> SaveAsync(RescueCardMetadata metadata, byte[]? pdfContentOrNull, CancellationToken ct)
    {
        var modelFolder = GetModelFolder(metadata.Brand, metadata.ModelName);
        Directory.CreateDirectory(modelFolder);

        string? relativePdfPath = null;
        if (pdfContentOrNull is not null)
        {
            var pdfPath = Path.Combine(modelFolder, $"{metadata.Id}.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdfContentOrNull, ct);
            relativePdfPath = Path.GetRelativePath(options.RootPath, pdfPath).Replace('\\', '/');
        }

        var finalMetadata = metadata with { LocalPdfRelativePath = relativePdfPath };
        await WriteJsonAsync(GetMetadataPath(finalMetadata), finalMetadata, ct);
        return finalMetadata;
    }

    public async Task UpdateMetadataAsync(RescueCardMetadata metadata, CancellationToken ct) =>
        await WriteJsonAsync(GetMetadataPath(metadata), metadata, ct);

    public string? GetPdfPath(RescueCardMetadata metadata) =>
        metadata.LocalPdfRelativePath is null
            ? null
            : Path.Combine(options.RootPath, metadata.LocalPdfRelativePath.Replace('/', Path.DirectorySeparatorChar));

    public Task DeleteAsync(RescueCardMetadata metadata, CancellationToken ct)
    {
        var jsonPath = GetMetadataPath(metadata);
        if (File.Exists(jsonPath))
        {
            File.Delete(jsonPath);
        }

        var pdfPath = GetPdfPath(metadata);
        if (pdfPath is not null && File.Exists(pdfPath))
        {
            File.Delete(pdfPath);
        }

        // Deleting an entry's files can leave its model folder empty (e.g. `split porsche` removes
        // the combined "all-models" entry once every page has been redistributed into per-model
        // folders) - clean that up rather than leaving a stray empty directory behind.
        var modelFolder = GetModelFolder(metadata.Brand, metadata.ModelName);
        if (Directory.Exists(modelFolder) && !Directory.EnumerateFileSystemEntries(modelFolder).Any())
        {
            Directory.Delete(modelFolder);
        }

        return Task.CompletedTask;
    }

    public async Task WriteBrandManifestAsync(Brand brand, CancellationToken ct)
    {
        var brandFolder = GetBrandFolder(brand);
        Directory.CreateDirectory(brandFolder);

        var all = new List<RescueCardMetadata>();
        if (Directory.Exists(brandFolder))
        {
            foreach (var file in EnumerateMetadataFiles(brandFolder))
            {
                var item = await ReadJsonAsync(file, ct);
                if (item is not null)
                {
                    all.Add(item);
                }
            }
        }

        await WriteJsonAsync(Path.Combine(brandFolder, ManifestFileName), all, ct);
    }

    public async Task<IReadOnlyList<RescueCardMetadata>> LoadAllAsync(CancellationToken ct)
    {
        var results = new List<RescueCardMetadata>();
        if (!Directory.Exists(options.RootPath))
        {
            return results;
        }

        foreach (var file in EnumerateMetadataFiles(options.RootPath))
        {
            var item = await ReadJsonAsync(file, ct);
            if (item is not null)
            {
                results.Add(item);
            }
        }

        return results;
    }

    private static IEnumerable<string> EnumerateMetadataFiles(string root) =>
        Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase));

    private string GetBrandFolder(Brand brand) =>
        Path.Combine(options.RootPath, brand.ToString().ToLowerInvariant());

    private string GetModelFolder(Brand brand, string? modelName) =>
        Path.Combine(GetBrandFolder(brand), RescueCardIdBuilder.BuildModelFolderSlug(modelName));

    private string GetMetadataPath(RescueCardMetadata metadata) =>
        Path.Combine(GetModelFolder(metadata.Brand, metadata.ModelName), $"{metadata.Id}.json");

    private static Task WriteJsonAsync<T>(string path, T value, CancellationToken ct) =>
        AtomicFileWriter.WriteJsonAsync(path, value, ct);

    /// <summary>
    /// Returns null both when a file legitimately isn't valid JSON for this type (shouldn't happen for
    /// writes made by this store, but can for a file truncated by a crash mid-write predating the
    /// atomic-write fix, or external interference) and lets the caller skip it - reported via stderr
    /// rather than thrown, so one damaged sidecar doesn't abort loading every other card in the store
    /// (see AtomicFileWriter for why new writes shouldn't produce truncated files going forward).
    /// </summary>
    private static async Task<RescueCardMetadata?> ReadJsonAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<RescueCardMetadata>(stream, JsonDefaults.Options, ct);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Console.Error.WriteLine(Strings.Get("Store_CorruptRescueCardSkipped", path, ex.Message));
            return null;
        }
    }
}
