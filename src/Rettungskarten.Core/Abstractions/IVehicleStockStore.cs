using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Abstractions;

public interface IVehicleStockStore
{
    Task SaveAsync(VehicleStockResult result, byte[] rawFileContent, string rawFileName, CancellationToken ct);

    /// <summary>Loads the most recently stored stock result, or a specific year if given.</summary>
    Task<VehicleStockResult?> LoadAsync(int? year, CancellationToken ct);
}
