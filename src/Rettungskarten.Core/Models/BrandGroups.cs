namespace Rettungskarten.Core.Models;

/// <summary>
/// Static brand -> corporate group and brand -> parent brand mapping. The parent brand is the brand a
/// sub-brand's vehicles are registered under in the KBA statistics (Cupra -> SEAT, Mercedes-AMG ->
/// Mercedes-Benz, Abarth -> Fiat, ...), used by <see cref="Matching.BrandNames"/> so a sub-brand's
/// cards still match stock rows; it is not used for anything else.
/// </summary>
public static class BrandGroups
{
    private static readonly Dictionary<Brand, ManufacturerGroup> Groups = new()
    {
        [Brand.VW] = ManufacturerGroup.VolkswagenGroup,
        [Brand.Audi] = ManufacturerGroup.VolkswagenGroup,
        [Brand.Skoda] = ManufacturerGroup.VolkswagenGroup,
        [Brand.Seat] = ManufacturerGroup.VolkswagenGroup,
        [Brand.Cupra] = ManufacturerGroup.VolkswagenGroup,
        [Brand.Porsche] = ManufacturerGroup.VolkswagenGroup,
        [Brand.Bentley] = ManufacturerGroup.VolkswagenGroup,
        [Brand.Lamborghini] = ManufacturerGroup.VolkswagenGroup,

        [Brand.MercedesBenz] = ManufacturerGroup.MercedesBenzGroup,
        [Brand.MercedesAmg] = ManufacturerGroup.MercedesBenzGroup,
        [Brand.MercedesEq] = ManufacturerGroup.MercedesBenzGroup,
        [Brand.Maybach] = ManufacturerGroup.MercedesBenzGroup,
        // Default for smart's pre-2022 (Mercedes-built) cards; the source for the post-2022 Geely
        // joint-venture models overrides this per entry (RescueCardEntry.ManufacturerGroupOverride).
        [Brand.Smart] = ManufacturerGroup.MercedesBenzGroup,

        [Brand.BMW] = ManufacturerGroup.BmwGroup,
        [Brand.Mini] = ManufacturerGroup.BmwGroup,
        [Brand.RollsRoyce] = ManufacturerGroup.BmwGroup,

        [Brand.Peugeot] = ManufacturerGroup.Stellantis,
        [Brand.Citroen] = ManufacturerGroup.Stellantis,
        [Brand.DS] = ManufacturerGroup.Stellantis,
        [Brand.Opel] = ManufacturerGroup.Stellantis,
        [Brand.Fiat] = ManufacturerGroup.Stellantis,
        [Brand.FiatProfessional] = ManufacturerGroup.Stellantis,
        [Brand.Abarth] = ManufacturerGroup.Stellantis,
        [Brand.AlfaRomeo] = ManufacturerGroup.Stellantis,
        [Brand.Lancia] = ManufacturerGroup.Stellantis,
        [Brand.Jeep] = ManufacturerGroup.Stellantis,
        [Brand.Dodge] = ManufacturerGroup.Stellantis,
        [Brand.Maserati] = ManufacturerGroup.Stellantis,

        [Brand.Renault] = ManufacturerGroup.RenaultGroup,
        [Brand.Dacia] = ManufacturerGroup.RenaultGroup,

        [Brand.Hyundai] = ManufacturerGroup.HyundaiMotorGroup,
        [Brand.Kia] = ManufacturerGroup.HyundaiMotorGroup,

        [Brand.Toyota] = ManufacturerGroup.ToyotaGroup,
        [Brand.Lexus] = ManufacturerGroup.ToyotaGroup,
        [Brand.Daihatsu] = ManufacturerGroup.ToyotaGroup,

        [Brand.Volvo] = ManufacturerGroup.Geely,
        [Brand.Polestar] = ManufacturerGroup.Geely,

        [Brand.Jaguar] = ManufacturerGroup.TataMotors,
        [Brand.LandRover] = ManufacturerGroup.TataMotors,

        [Brand.Chevrolet] = ManufacturerGroup.GeneralMotors,
        [Brand.Cadillac] = ManufacturerGroup.GeneralMotors,

        [Brand.MG] = ManufacturerGroup.SaicMotor,
        [Brand.Maxus] = ManufacturerGroup.SaicMotor
    };

    private static readonly Dictionary<Brand, Brand> Parents = new()
    {
        [Brand.Cupra] = Brand.Seat,
        [Brand.MercedesAmg] = Brand.MercedesBenz,
        [Brand.MercedesEq] = Brand.MercedesBenz,
        [Brand.Maybach] = Brand.MercedesBenz,
        [Brand.FiatProfessional] = Brand.Fiat,
        [Brand.Abarth] = Brand.Fiat
    };

    /// <summary>The brand's default group; every brand not listed explicitly is
    /// <see cref="ManufacturerGroup.Independent"/>.</summary>
    public static ManufacturerGroup GroupOf(Brand brand) =>
        Groups.GetValueOrDefault(brand, ManufacturerGroup.Independent);

    /// <summary>The brand whose KBA stock rows this brand's vehicles are counted under, or null if the
    /// brand is tracked under its own name.</summary>
    public static Brand? ParentBrandOf(Brand brand) =>
        Parents.TryGetValue(brand, out var parent) ? parent : null;
}
