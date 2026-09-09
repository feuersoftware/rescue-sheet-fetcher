using System.Text.Json;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.Storage;

public sealed record VehicleStockStoreOptions
{
    public string RootPath { get; init; } = Path.Combine("data", "stock");
}

public sealed record VehicleStockMetaFile(
    string SourceUrl, string License, DateTimeOffset FetchedAtUtc, int RowCount, int WarningCount);

/// <summary>Layout: {root}/{year}/fz12_{year}.xlsx (raw) + .json (parsed) + .meta.json (provenance).</summary>
public sealed class FileSystemVehicleStockStore(VehicleStockStoreOptions options) : IVehicleStockStore
{
    public async Task SaveAsync(VehicleStockResult result, byte[] rawFileContent, string rawFileName, CancellationToken ct)
    {
        var yearFolder = Path.Combine(options.RootPath, result.Year.ToString());
        Directory.CreateDirectory(yearFolder);

        await File.WriteAllBytesAsync(Path.Combine(yearFolder, rawFileName), rawFileContent, ct);

        await using (var stream = File.Create(Path.Combine(yearFolder, $"fz12_{result.Year}.json")))
        {
            await JsonSerializer.SerializeAsync(stream, result, JsonDefaults.Options, ct);
        }

        var meta = new VehicleStockMetaFile(
            result.SourceUrl, result.License, DateTimeOffset.UtcNow, result.Rows.Count, result.UnparsedRowWarnings.Count);

        await using (var stream = File.Create(Path.Combine(yearFolder, $"fz12_{result.Year}.meta.json")))
        {
            await JsonSerializer.SerializeAsync(stream, meta, JsonDefaults.Options, ct);
        }
    }

    public async Task<VehicleStockResult?> LoadAsync(int? year, CancellationToken ct)
    {
        if (!Directory.Exists(options.RootPath))
        {
            return null;
        }

        var resolvedYear = year ?? Directory.GetDirectories(options.RootPath)
            .Select(d => int.TryParse(Path.GetFileName(d), out var y) ? y : (int?)null)
            .Where(y => y is not null)
            .Select(y => y!.Value)
            .OrderDescending()
            .FirstOrDefault();

        if (resolvedYear == 0)
        {
            return null;
        }

        var resultPath = Path.Combine(options.RootPath, resolvedYear.ToString(), $"fz12_{resolvedYear}.json");
        if (!File.Exists(resultPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(resultPath);
        return await JsonSerializer.DeserializeAsync<VehicleStockResult>(stream, JsonDefaults.Options, ct);
    }
}
