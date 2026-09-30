namespace Rettungskarten.Core.Models;

public enum BrandRunOutcome
{
    Completed,
    DiscoveryFailed,
    NotImplemented
}

public sealed record ModelRunResult(RescueCardEntry Entry, RescueCardStatus Status, string? FailureReason);

public sealed record BrandRunResult(
    Brand Brand,
    BrandRunOutcome Outcome,
    IReadOnlyList<ModelRunResult> Models,
    string? Note)
{
    public static BrandRunResult Completed(Brand brand, IReadOnlyList<ModelRunResult> models) =>
        new(brand, BrandRunOutcome.Completed, models, null);

    /// <summary>At least one of the brand's sources failed discovery while others (if any) still
    /// produced <paramref name="models"/> - the run is still reported as failed so the link check
    /// notices, but the surviving sources' cards are kept.</summary>
    public static BrandRunResult DiscoveryFailed(Brand brand, IReadOnlyList<ModelRunResult> models, string note) =>
        new(brand, BrandRunOutcome.DiscoveryFailed, models, note);

    public static BrandRunResult NotImplemented(Brand brand, string note) =>
        new(brand, BrandRunOutcome.NotImplemented, [], note);

    public int Discovered => Models.Count;
    public int Downloaded => Models.Count(m => m.Status == RescueCardStatus.Downloaded);
    public int MetadataOnly => Models.Count(m => m.Status == RescueCardStatus.MetadataOnly);

    /// <summary>Entries with no discoverable download URL at all - distinct from <see cref="MetadataOnly"/>
    /// (a URL was found but the fetch itself failed). Before <see cref="ModelRunResult"/> carried the
    /// full <see cref="RescueCardStatus"/> instead of a bare bool, the console run-summary table
    /// conflated both cases into one "Metadata only" number, hiding the difference between "this
    /// brand's discovery is fundamentally incomplete" and "isolated download failures".</summary>
    public int Failed => Models.Count(m => m.Status == RescueCardStatus.Failed);
}
