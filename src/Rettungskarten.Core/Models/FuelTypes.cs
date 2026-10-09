namespace Rettungskarten.Core.Models;

/// <summary>
/// The one vocabulary <c>fuelType</c> is stored in. Every source reads the drivetrain from its own
/// wording - "PHEV" (BMW), "Plug-in-Hybrid" (Stellantis, Honda), "Plug-in Hybrid" (Renault, Toyota),
/// "Elektro"/"Electric"/"BEV", the industry filename convention's "GD" and "Hybrid-Electric", BYD's
/// "DM-i" - and without one normalization point the same drivetrain was persisted under a dozen
/// spellings, so anyone filtering cards by fuel type (e.g. "every high-voltage vehicle") silently missed
/// some. The orchestrator and <c>split</c> normalize every value through <see cref="Normalize"/> before it
/// is saved; parsers keep their own wording.
///
/// Values are invariant English, like model names: <see cref="Petrol"/>, <see cref="Diesel"/>,
/// <see cref="PetrolOrDiesel"/> (the convention's "GD" - one sheet for both), <see cref="Electric"/>,
/// <see cref="Hybrid"/> (also the convention's "Hybrid-Electric", which doesn't say whether it plugs in),
/// <see cref="MildHybrid"/>, <see cref="PlugInHybrid"/>, <see cref="Hydrogen"/>, <see cref="Cng"/>,
/// <see cref="Lpg"/>, <see cref="Ethanol"/>. A value not listed here (a combined pictogram text such as
/// Mercedes' "Hybrid Benzin") is kept as the source wrote it rather than guessed at.
/// </summary>
public static class FuelTypes
{
    public const string Petrol = "Petrol";
    public const string Diesel = "Diesel";
    public const string PetrolOrDiesel = "Petrol/Diesel";
    public const string Electric = "Electric";
    public const string Hybrid = "Hybrid";
    public const string MildHybrid = "Mild Hybrid";
    public const string PlugInHybrid = "Plug-in Hybrid";
    public const string Hydrogen = "Hydrogen";
    public const string Cng = "CNG";
    public const string Lpg = "LPG";
    public const string Ethanol = "Ethanol";

    private static readonly Dictionary<string, string> Spellings = Build(
        (Petrol, ["Petrol", "Benzin", "Benziner", "Gasoline", "Essence"]),
        (Diesel, ["Diesel"]),
        (PetrolOrDiesel, ["GD", "ICE", "Petrol/Diesel", "Benzin/Diesel", "Diesel/Benzin", "Diesel / Benzin", "Benzin / Diesel"]),
        (Electric, ["Electric", "Elektro", "Elektrisch", "BEV", "EV", "Elettrica", "Electrique", "Électrique"]),
        (Hybrid, ["Hybrid", "HEV", "Hybride", "Hybrid-Electric", "Hybrid Electric", "Hybrid (Electric)", "Full Hybrid", "Vollhybrid", "FHEV", "FHybrid", "e:HEV", "EHEV", "Ibrida"]),
        (MildHybrid, ["Mild Hybrid", "Mild-Hybrid", "MHEV", "48V-Hybrid", "48V Hybrid"]),
        // Ford's "Energi" badge (C-MAX/Fusion Energi) is its plug-in hybrid.
        (PlugInHybrid, ["Plug-in Hybrid", "Plug-in-Hybrid", "PHEV", "P:HEV", "Ibrida Plug-in", "DM-i", "Energi"]),
        (Hydrogen, ["Hydrogen", "Wasserstoff", "FCEV", "Fuel Cell", "Brennstoffzelle", "Hydrogène", "Hydrogene"]),
        (Cng, ["CNG", "Erdgas", "Natural Power", "Compressed Natural Gas"]),
        (Lpg, ["LPG", "Autogas"]),
        (Ethanol, ["Ethanol", "E85"]));

    public static string? Normalize(string? fuelType)
    {
        if (string.IsNullOrWhiteSpace(fuelType))
        {
            return null;
        }

        var trimmed = fuelType.Trim();
        return Spellings.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }

    // Case-insensitive: "Plug-In-Hybrid", "ELEKTRO", "phev" match the entries above.
    private static Dictionary<string, string> Build(params (string Canonical, string[] Spellings)[] groups)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonical, spellings) in groups)
        {
            foreach (var spelling in spellings)
            {
                map.Add(spelling, canonical);
            }
        }

        return map;
    }
}
