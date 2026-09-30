namespace Rettungskarten.Core.Models;

/// <summary>What one rescue-card entry's PDF actually covers.</summary>
public enum DocumentScope
{
    /// <summary>One model (or one model generation/variant) per file - the normal case.</summary>
    Single,

    /// <summary>One file covering many models (e.g. Ford's or Porsche's "all models" PDF). Kept as
    /// its own entry even after `split` has produced per-model parts from it.</summary>
    Combined,

    /// <summary>A per-model PDF cut out of a <see cref="Combined"/> document by the `split` command;
    /// <see cref="RescueCardMetadata.SplitSourceId"/> points back at that combined entry.</summary>
    SplitPart
}
