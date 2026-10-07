using System.Text.Json;
using System.Text.Json.Serialization;
using Rettungskarten.Core.Config;

namespace Rettungskarten.Infrastructure.Config;

/// <summary>Loads the hand-curated sibling-models/model-aliases/kba-unlisted-models JSON files bundled with the app.</summary>
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

    /// <summary>Resolves the default bundled config path next to the running assembly.</summary>
    public static string DefaultSiblingModelsPath() =>
        Path.Combine(AppContext.BaseDirectory, "Config", "sibling-models.json");

    public static string DefaultModelAliasesPath() =>
        Path.Combine(AppContext.BaseDirectory, "Config", "model-aliases.json");

    public static string DefaultKbaUnlistedModelsPath() =>
        Path.Combine(AppContext.BaseDirectory, "Config", "kba-unlisted-models.json");
}
