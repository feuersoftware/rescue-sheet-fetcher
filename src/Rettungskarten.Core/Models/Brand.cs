namespace Rettungskarten.Core.Models;

/// <summary>
/// One value per brand exactly as the manufacturer itself presents it - sub-brands that the
/// manufacturer publishes rescue sheets for separately (Mercedes-AMG, Mercedes-EQ, Maybach, Fiat
/// Professional, Cupra) get their own value, like Cupra/SEAT always did. Which group a brand
/// belongs to and which brand's KBA figures it falls under are tracked separately in
/// <see cref="BrandGroups"/>, not by collapsing brands here.
///
/// Every value is also a valid <c>--brand</c> CLI argument (lower-cased), and <c>--brand all</c>
/// iterates this enum - so adding a value here automatically adds it to the weekly link check.
/// </summary>
public enum Brand
{
    // Volkswagen Group
    VW,
    Audi,
    Skoda,
    Seat,
    Cupra,
    Porsche,
    Bentley,
    Lamborghini,

    // Mercedes-Benz Group
    MercedesBenz,
    MercedesAmg,
    MercedesEq,
    Maybach,
    Smart,

    // BMW Group
    BMW,
    Mini,
    RollsRoyce,

    // Stellantis
    Peugeot,
    Citroen,
    DS,
    Opel,
    Fiat,
    FiatProfessional,
    Abarth,
    AlfaRomeo,
    Lancia,
    Jeep,
    Dodge,
    Maserati,

    // Renault Group
    Renault,
    Dacia,

    // Hyundai Motor Group
    Hyundai,
    Kia,

    // Toyota Group
    Toyota,
    Lexus,
    Daihatsu,

    // Geely
    Volvo,
    Polestar,

    // Tata Motors (JLR)
    Jaguar,
    LandRover,

    // General Motors (EU legacy)
    Chevrolet,
    Cadillac,

    // SAIC Motor
    MG,
    Maxus,

    // Independent manufacturers
    Ford,
    Nissan,
    Mitsubishi,
    Mazda,
    Honda,
    Subaru,
    Suzuki,
    Isuzu,
    Tesla,
    KGM,
    BYD,
    Ruf,
    Saab,
    Streetscooter
}
