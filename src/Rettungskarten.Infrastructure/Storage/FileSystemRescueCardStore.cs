using System.Text.Json;
using Rettungskarten.Core.Abstractions;
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

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonDefaults.Options, ct);
    }

    private static async Task<RescueCardMetadata?> ReadJsonAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<RescueCardMetadata>(stream, JsonDefaults.Options, ct);
    }
}
