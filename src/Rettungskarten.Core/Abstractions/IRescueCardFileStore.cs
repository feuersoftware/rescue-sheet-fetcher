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
}
