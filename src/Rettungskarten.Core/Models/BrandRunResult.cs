namespace Rettungskarten.Core.Models;

public enum BrandRunOutcome
{
    Completed,
    DiscoveryFailed,
    NotImplemented
}

public sealed record ModelRunResult(RescueCardEntry Entry, bool Downloaded, string? FailureReason);

public sealed record BrandRunResult(
    Brand Brand,
    BrandRunOutcome Outcome,
    IReadOnlyList<ModelRunResult> Models,
    string? Note)
{
    public static BrandRunResult Completed(Brand brand, IReadOnlyList<ModelRunResult> models) =>
        new(brand, BrandRunOutcome.Completed, models, null);

    public static BrandRunResult DiscoveryFailed(Brand brand, Exception ex) =>
        new(brand, BrandRunOutcome.DiscoveryFailed, [], ex.Message);

    public static BrandRunResult NotImplemented(Brand brand, string note) =>
        new(brand, BrandRunOutcome.NotImplemented, [], note);

    public int Discovered => Models.Count;
    public int Downloaded => Models.Count(m => m.Downloaded);
    public int MetadataOnly => Models.Count(m => !m.Downloaded);
}
