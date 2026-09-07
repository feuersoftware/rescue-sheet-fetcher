using Rettungskarten.Core.Matching;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Config;

public sealed record SiblingModelRef(Brand Brand, string ModelName, string? Notes = null);

public sealed record SiblingGroup(string GroupId, string PlatformName, IReadOnlyList<SiblingModelRef> Members);

/// <summary>
/// Hand-curated platform-sharing ("Schwestermodelle") data. No source publishes this — it's automotive
/// domain knowledge that must be maintained manually in sibling-models.json.
/// </summary>
public sealed record SiblingModelsConfig(IReadOnlyList<SiblingGroup> Groups)
{
    public IReadOnlyList<string> FindGroupIds(Brand brand, string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return [];
        }

        var normalized = ModelNameNormalizer.Normalize(modelName);
        return Groups
            .Where(g => g.Members.Any(m => m.Brand == brand && ModelNameNormalizer.Normalize(m.ModelName) == normalized))
            .Select(g => g.GroupId)
            .ToList();
    }

    public static readonly SiblingModelsConfig Empty = new([]);
}
