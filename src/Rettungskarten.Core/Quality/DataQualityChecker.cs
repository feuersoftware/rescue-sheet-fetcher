using System.Text.RegularExpressions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Quality;

public enum DataQualityIssueKind
{
    BodyTypeLooksLikeYear,
    FuelTypeIsBareDigits,
    DuplicateId,
    BrandEntirelyUnknownPriority,
    BrandUnknownPriorityCountAboveBaseline
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
///
/// The BrandUnknownPriorityCountAboveBaseline check covers the gap BrandEntirelyUnknownPriority can't
/// see: a brand that is *partially*, permanently unmatched by design rather than 100% unmatched by bug.
/// Porsche is the known case (see model-aliases.json and commit b19a397's message) - its ultra-low-
/// volume specials (GT2/GT2 RS/GT3/GT3 RS/R/Turbo, 718 Cayman GT4) are deliberately NOT aliased to a
/// base KBA series, since lumping them into that series' aggregate fleet count would overstate their
/// real commonality far more than for an ordinary trim. That fix took Porsche's Unknown count from
/// 65/85 to a stable 19/85 - KnownUnknownBaselines records that 19 so a *further* increase (a new
/// unmatched model, or an alias that broke) gets flagged instead of silently blending into "business as
/// usual", which is exactly the kind of partial, permanent gap that only firing at 100% would miss.
/// </summary>
public static class DataQualityChecker
{
    private static readonly Regex YearOrYearRangePattern = new(
        @"^(19|20)\d{2}(-(19|20)\d{2})?$", RegexOptions.Compiled);
    private static readonly Regex BareDigitsPattern = new(@"^\d+$", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<Brand, int> KnownUnknownBaselines = new Dictionary<Brand, int>
    {
        [Brand.Porsche] = 19
    };

    public static IReadOnlyList<DataQualityIssue> CheckAll(IReadOnlyList<RescueCardMetadata> cards)
    {
        var issues = new List<DataQualityIssue>();

        foreach (var card in cards)
        {
            if (card.BodyType is not null && YearOrYearRangePattern.IsMatch(card.BodyType))
            {
                issues.Add(new DataQualityIssue(card.Id, card.Brand, DataQualityIssueKind.BodyTypeLooksLikeYear,
                    Strings.Get("Quality_BodyTypeLooksLikeYear", card.BodyType)));
            }

            if (card.FuelType is not null && BareDigitsPattern.IsMatch(card.FuelType))
            {
                issues.Add(new DataQualityIssue(card.Id, card.Brand, DataQualityIssueKind.FuelTypeIsBareDigits,
                    Strings.Get("Quality_FuelTypeIsBareDigits", card.FuelType)));
            }
        }

        foreach (var duplicateGroup in cards.GroupBy(c => c.Id).Where(g => g.Count() > 1))
        {
            issues.Add(new DataQualityIssue(duplicateGroup.Key, duplicateGroup.First().Brand,
                DataQualityIssueKind.DuplicateId,
                Strings.Get("Quality_DuplicateId", duplicateGroup.Key, duplicateGroup.Count())));
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
                        Strings.Get("Quality_BrandEntirelyUnknownPriority", brandGroup.Count(), brandGroup.Key)));
                }
                else if (KnownUnknownBaselines.TryGetValue(brandGroup.Key, out var baseline))
                {
                    var unknownCount = brandGroup.Count(c => c.BundlePriority == BundlePriority.Unknown);
                    if (unknownCount > baseline)
                    {
                        issues.Add(new DataQualityIssue(brandGroup.Key.ToString(), brandGroup.Key,
                            DataQualityIssueKind.BrandUnknownPriorityCountAboveBaseline,
                            Strings.Get("Quality_BrandUnknownPriorityCountAboveBaseline", brandGroup.Key, unknownCount, baseline)));
                    }
                }
            }
        }

        return issues;
    }
}
