using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.IfzBerlin;

namespace Rettungskarten.Tests.RescueCards;

/// <summary>auto_typ values and labels below are verbatim from the real IFZ Berlin API.</summary>
public class IfzBerlinLabelParserTests
{
    [Theory]
    [InlineData("Astra_L", "Astra", "L", false)]
    [InlineData("Insignia-B", "Insignia", "B", false)]
    [InlineData("Crossland X", "Crossland X", null, false)]
    [InlineData("Movano_e", "Movano", null, true)]
    [InlineData("Ampera-e", "Ampera-e", null, true)]
    [InlineData("Opel_Rocks_e", "Rocks-e", null, true)]
    [InlineData("Opel_GT", "GT", null, false)]
    [InlineData("OPEL_Speedster", "Speedster", null, false)]
    [InlineData("Zafira_D", "Zafira Life", "D", false)]
    [InlineData("Modell_9-3", "9-3", null, false)]
    [InlineData("Calibra_Coupe", "Calibra", null, false)]
    public void FromAutoTyp_SplitsModelGenerationAndElectricSuffix(string autoTyp, string model, string? generation, bool electric)
    {
        var result = IfzBerlinLabelParser.FromAutoTyp(autoTyp);

        Assert.Equal(model, result.ModelName);
        Assert.Equal(generation, result.Generation);
        Assert.Equal(electric, result.IsElectric);
    }

    [Fact]
    public void Parse_GermanLabel_ReadsBodyFuelDoorsAndYear()
    {
        var parsed = IfzBerlinLabelParser.Parse("Corsa_D", "Corsa D 5-Türer Autogas (LPG) (2010)", "ret_deu_pdf/german_opelvauxhall_corsa_d_3", "DE");

        Assert.Equal("Corsa", parsed.ModelName);
        Assert.Equal(5, parsed.Doors);
        Assert.Equal("LPG", parsed.FuelType);
        Assert.Equal(2010, parsed.BuildYearFrom);
        Assert.Null(parsed.BuildYearTo);
        Assert.Equal(ParseConfidence.High, parsed.ParseConfidence);
    }

    [Fact]
    public void Parse_EnglishLabel_NormalizesBodyAndFuelWording()
    {
        var parsed = IfzBerlinLabelParser.Parse("Zafira_B", "Zafira B Compressed Natural Gas (CNG) (2005)", "ret_eng_pdf/english_opelvauxhall_zafira_b_3", "EN");

        Assert.Equal("CNG", parsed.FuelType);
        Assert.Equal("EN", parsed.LanguageCode);

        var estate = IfzBerlinLabelParser.Parse("Astra_J", "Astra J Estate (Sports Tourer) (2010)", "ret_eng_pdf/english_opelvauxhall_astra_j_3", "EN");
        Assert.Equal("Kombi", estate.BodyType);

        var threeDoor = IfzBerlinLabelParser.Parse("Adam", "Adam 3-door (2013)", "ret_eng_pdf/english_opelvauxhall_adam_1", "EN");
        Assert.Equal(3, threeDoor.Doors);
    }

    [Fact]
    public void Parse_FaceliftYearAndHydrogen()
    {
        var insignia = IfzBerlinLabelParser.Parse("Insignia", "Insignia 4-Türer (2008/2013)", "ret_deu_pdf/german_opelvauxhall_insignia_1", "DE");
        Assert.Equal(2008, insignia.BuildYearFrom);

        var vivaro = IfzBerlinLabelParser.Parse("Vivaro_C", "Vivaro_C Hydrogen electric Fuel Cell 11,5 KWh (2021)", "ret_deu_pdf/deu_ov_vivaro_c_e_fuel_cell_van", "DE");
        Assert.Equal("Vivaro", vivaro.ModelName);
        Assert.Equal("Wasserstoff", vivaro.FuelType);
    }

    [Fact]
    public void Parse_GenerationLetterEInFileNameIsNotElectric()
    {
        var parsed = IfzBerlinLabelParser.Parse("Corsa_E", "Corsa E 3-Türer (2014)", "ret_deu_pdf/german_opelvauxhall_corsa_e_1", "DE");

        Assert.Null(parsed.FuelType);
    }

    [Fact]
    public void Parse_MissingLabel_FallsBackToGroupAndFileName()
    {
        var parsed = IfzBerlinLabelParser.Parse("Vivaro_C", "  ", "ret_deu_pdf/deu_ov_vivaro_c_cargo_e_life_50", "DE");

        Assert.Equal("Vivaro", parsed.ModelName);
        Assert.Equal("Elektro", parsed.FuelType);
        Assert.Equal("Kastenwagen", parsed.BodyType);
        Assert.Equal(ParseConfidence.Heuristic, parsed.ParseConfidence);
    }
}
