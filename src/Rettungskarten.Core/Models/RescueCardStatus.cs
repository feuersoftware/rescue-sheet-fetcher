namespace Rettungskarten.Core.Models;

public enum RescueCardStatus
{
    /// <summary>PDF was discovered and successfully downloaded.</summary>
    Downloaded,

    /// <summary>PDF was discovered but the download failed (e.g. HTTP 403); metadata was still persisted.</summary>
    MetadataOnly,

    /// <summary>Discovery itself failed for this entry.</summary>
    Failed,

    /// <summary>The brand has no working source implementation yet (e.g. Porsche).</summary>
    NotImplemented
}
