using System.Text.Json;
using System.Text.Json.Serialization;
using Rettungskarten.Core.Config;

namespace Rettungskarten.Infrastructure.Config;

/// <summary>Loads the hand-curated sibling-models/model-aliases JSON files bundled with the app.</summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<SiblingModelsConfig> LoadSiblingModelsAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return SiblingModelsConfig.Empty;
        }

        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<SiblingModelsConfig>(stream, JsonOptions, ct);
        return config ?? SiblingModelsConfig.Empty;
    }

    public static async Task<ModelAliasConfig> LoadModelAliasesAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return ModelAliasConfig.Empty;
        }

        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<ModelAliasConfig>(stream, JsonOptions, ct);
        return config ?? ModelAliasConfig.Empty;
    }

    /// <summary>Resolves the default bundled config path next to the running assembly.</summary>
    public static string DefaultSiblingModelsPath() =>
        Path.Combine(AppContext.BaseDirectory, "Config", "sibling-models.json");

    public static string DefaultModelAliasesPath() =>
        Path.Combine(AppContext.BaseDirectory, "Config", "model-aliases.json");
}
