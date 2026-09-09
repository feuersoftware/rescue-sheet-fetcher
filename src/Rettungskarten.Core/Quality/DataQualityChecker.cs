using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Quality;

public enum DataQualityIssueKind
{
    BodyTypeLooksLikeYear,
    FuelTypeIsBareDigits,
    DuplicateId,
    BrandEntirelyUnknownPriority
}

public sealed record DataQualityIssue(string CardId, Brand Brand, DataQualityIssueKind Kind, string Description);

/// <summary>
/// Checks already-persisted rescue-card metadata for the kind of anomaly that, this session, was only
/// ever found by manually eyeballing a full-brand run (a real bug: a filename-parser off-by-one shifted
/// a model year into the BodyType field for four Audi cards, giving BodyType values like "2018" and
/// "2019-2023" - see AudiFilenameParser's doc comment for the fix). Pure logic, no I/O: the caller loads
/// the cards (e.g. via IRescueCardFileStore.LoadAllAsync) and passes them in.
///
/// The BrandEntirelyUnknownPriority check only means anything once <c>prioritize</c> has actually run
/// (BundlePriority stays Unknown for every brand until then) - it only fires when at least one *other*
/// brand in the same list has a non-Unknown priority, so a dataset where prioritize simply hasn't run
/// yet at all doesn't itself look like a bug. This is the shape of a real bug this session found (Cupra
/// matching zero KBA stock rows because KBA tracks it under "SEAT", not "CUPRA" - see BrandNames.cs).
///
/// The DuplicateId check is cheap defense-in-depth, not something that can observe an id collision
/// within a single FileSystemRescueCardStore run: that store derives each card's on-disk path directly
/// from its Id, so two cards that hash-collide to the same Id would already have overwritten each other
/// on disk by the time LoadAllAsync runs - only one card, one Id, survives to be loaded. This check only
/// has something to find if the caller passes in cards merged from more than one store/run.
/// </summary>
public static class DataQualityChecker
{
    private static readonly Regex YearOrYearRangePattern = new(
        @"^(19|20)\d{2}(-(19|20)\d{2})?$", RegexOptions.Compiled);
    private static readonly Regex BareDigitsPattern = new(@"^\d+$", RegexOptions.Compiled);

    public static IReadOnlyList<DataQualityIssue> CheckAll(IReadOnlyList<RescueCardMetadata> cards)
    {
        var issues = new List<DataQualityIssue>();

        foreach (var card in cards)
        {
            if (card.BodyType is not null && YearOrYearRangePattern.IsMatch(card.BodyType))
            {
                issues.Add(new DataQualityIssue(card.Id, card.Brand, DataQualityIssueKind.BodyTypeLooksLikeYear,
                    $"BodyType '{card.BodyType}' looks like a year or year range, not a body style - probably a shifted field."));
            }

            if (card.FuelType is not null && BareDigitsPattern.IsMatch(card.FuelType))
            {
                issues.Add(new DataQualityIssue(card.Id, card.Brand, DataQualityIssueKind.FuelTypeIsBareDigits,
                    $"FuelType '{card.FuelType}' is just digits, not a fuel/drivetrain description - probably a shifted field."));
            }
        }

        foreach (var duplicateGroup in cards.GroupBy(c => c.Id).Where(g => g.Count() > 1))
        {
            issues.Add(new DataQualityIssue(duplicateGroup.Key, duplicateGroup.First().Brand,
                DataQualityIssueKind.DuplicateId,
                $"Id '{duplicateGroup.Key}' appears {duplicateGroup.Count()} times in this dataset."));
        }

        var anyPriorityAssigned = cards.Any(c => c.BundlePriority != BundlePriority.Unknown);
        if (anyPriorityAssigned)
        {
            foreach (var brandGroup in cards.GroupBy(c => c.Brand))
            {
                if (brandGroup.All(c => c.BundlePriority == BundlePriority.Unknown))
                {
                    issues.Add(new DataQualityIssue(brandGroup.Key.ToString(), brandGroup.Key,
                        DataQualityIssueKind.BrandEntirelyUnknownPriority,
                        $"Every one of {brandGroup.Count()} {brandGroup.Key} cards has BundlePriority.Unknown, even though other brands in this dataset matched real KBA stock rows - likely a brand-matching gap (e.g. a missing BrandNames alias), not genuinely 100% untracked models."));
                }
            }
        }

        return issues;
    }
}
