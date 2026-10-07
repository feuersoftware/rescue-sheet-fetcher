using System.Text.Json;
using System.Text.Json.Serialization;
using Rettungskarten.Core.Config;

namespace Rettungskarten.Infrastructure.Config;

/// <summary>Loads the hand-curated JSON config files bundled with the app (see the Config folder).</summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Task<SiblingModelsConfig> LoadSiblingModelsAsync(string path, CancellationToken ct) =>
        LoadAsync(path, SiblingModelsConfig.Empty, ct);

    public static Task<ModelAliasConfig> LoadModelAliasesAsync(string path, CancellationToken ct) =>
        LoadAsync(path, ModelAliasConfig.Empty, ct);

    public static Task<KbaUnlistedModelConfig> LoadKbaUnlistedModelsAsync(string path, CancellationToken ct) =>
        LoadAsync(path, KbaUnlistedModelConfig.Empty, ct);

    private static async Task<T> LoadAsync<T>(string path, T empty, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return empty;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct) ?? empty;
    }

    /// <summary>Synchronous, since the split layouts are plain synchronous objects; read once per run.</summary>
    public static PorscheHeaderlessSheetsConfig LoadPorscheHeaderlessSheets(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<PorscheHeaderlessSheetsConfig>(File.ReadAllText(path), JsonOptions) ?? PorscheHeaderlessSheetsConfig.Empty
            : PorscheHeaderlessSheetsConfig.Empty;

    /// <summary>Resolves the default bundled config path next to the running assembly.</summary>
    public static string DefaultSiblingModelsPath() =>
        Path.Combine(AppContext.BaseDirectory, "Config", "sibling-models.json");

    public static string DefaultModelAliasesPath() =>
        Path.Combine(AppContext.BaseDirectory, "Config", "model-aliases.json");

    public static string DefaultKbaUnlistedModelsPath() =>
        Path.Combine(AppContext.BaseDirectory, "Config", "kba-unlisted-models.json");

    public static string DefaultPorscheHeaderlessSheetsPath() =>
        Path.Combine(AppContext.BaseDirectory, "Config", "porsche-headerless-sheets.json");
}

/// <summary>A Porsche combined-PDF sheet without a model header, named by hand by its document ID
/// (see PorscheCombinedPdfLayout).</summary>
public sealed record PorscheHeaderlessSheet(string DocumentId, string ModelName, string? BodyType);

public sealed record PorscheHeaderlessSheetsConfig(IReadOnlyList<PorscheHeaderlessSheet> Sheets)
{
    public static readonly PorscheHeaderlessSheetsConfig Empty = new([]);
}
