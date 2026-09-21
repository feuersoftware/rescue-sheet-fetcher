using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Abstractions;

/// <summary>
/// Fetches the KBA FZ12 vehicle stock ("Bestand") file for a given reference year.
/// </summary>
public interface IVehicleStockSource
{
    Task<VehicleStockResult> FetchAsync(int year, CancellationToken ct);
}
