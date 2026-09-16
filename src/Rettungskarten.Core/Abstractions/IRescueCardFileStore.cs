using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Abstractions;

public interface IRescueCardFileStore
{
    /// <summary>
    /// Persists the metadata sidecar (always) and the PDF content (only if not null).
    /// Returns the metadata as actually written (with its resolved storage-relative paths).
    /// </summary>
    Task<RescueCardMetadata> SaveAsync(RescueCardMetadata metadata, byte[]? pdfContentOrNull, CancellationToken ct);

    /// <summary>Writes/refreshes the derived per-brand manifest aggregating all sidecars for that brand.</summary>
    Task WriteBrandManifestAsync(Brand brand, CancellationToken ct);

    /// <summary>Reads back all currently stored metadata sidecars (used by the prioritize command).</summary>
    Task<IReadOnlyList<RescueCardMetadata>> LoadAllAsync(CancellationToken ct);

    /// <summary>Overwrites an existing sidecar in place (used by the prioritize command to add fleet-size/priority).</summary>
    Task UpdateMetadataAsync(RescueCardMetadata metadata, CancellationToken ct);

    /// <summary>Removes an entry's sidecar and PDF (used by `split porsche` once a combined document has
    /// been split into per-model entries, so the redundant combined blob isn't kept alongside them).
    /// Also removes the entry's model folder if that leaves it empty - this is a general storage-layer
    /// invariant (no caller should ever see a stray empty folder after a delete), not specific to any
    /// one caller's usage pattern.</summary>
    Task DeleteAsync(RescueCardMetadata metadata, CancellationToken ct);

    /// <summary>Resolves an entry's PDF to a full on-disk path (or null if it was never downloaded).
    /// Shared by callers that need to read the file directly (e.g. `split porsche`) so the
    /// LocalPdfRelativePath -> full-path resolution logic exists in exactly one place.</summary>
    string? GetPdfPath(RescueCardMetadata metadata);
}
