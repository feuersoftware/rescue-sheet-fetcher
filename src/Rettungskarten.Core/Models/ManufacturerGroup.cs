namespace Rettungskarten.Core.Models;

/// <summary>
/// The corporate group a brand belongs to - metadata only (persisted in every sidecar, the brand
/// manifest and the priority report), it never changes how a card is discovered or matched. Joint
/// ventures and alliances are assigned to the legal majority owner (Renault-Nissan-Mitsubishi stay
/// separate); smart is the one brand whose group depends on the source (Mercedes-Benz up to 2021,
/// Geely's joint venture from 2022) - see <see cref="BrandGroups"/>.
/// </summary>
public enum ManufacturerGroup
{
    VolkswagenGroup,
    MercedesBenzGroup,
    BmwGroup,
    Stellantis,
    RenaultGroup,
    HyundaiMotorGroup,
    ToyotaGroup,
    Geely,
    TataMotors,
    GeneralMotors,
    SaicMotor,
    Independent
}
