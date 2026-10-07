using System.Text.RegularExpressions;
using Rettungskarten.Core.Config;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Matching;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Quality;

public enum DataQualityIssueKind
{
    BodyTypeLooksLikeYear,
    FuelTypeIsBareDigits,
    DuplicateId,
    UnmatchedModel,
    UnlistedModelMatched
}

public enum DataQualityIssueSeverity
{
    Error,
    Warning
}

public sealed record DataQualityIssue(
    string CardId, Brand Brand, DataQualityIssueKind Kind, string Description,
    DataQualityIssueSeverity Severity = DataQualityIssueSeverity.Error);

/// <summary>
/// Checks already-persisted rescue-card metadata for the kind of anomaly that, this session, was only
/// ever found by manually eyeballing a full-brand run (a real bug: a filename-parser off-by-one shifted
/// a model year into the BodyType field for four Audi cards, giving BodyType values like "2018" and
/// "2019-2023" - see AudiFilenameParser's doc comment for the fix). Pure logic, no I/O: the caller loads
/// the cards (e.g. via IRescueCardFileStore.LoadAllAsync) and passes them in.
///
/// The DuplicateId check is cheap defense-in-depth, not something that can observe an id collision
/// within a single FileSystemRescueCardStore run: that store derives each card's on-disk path directly
/// from its Id, so two cards that hash-collide to the same Id would already have overwritten each other
/// on disk by the time LoadAllAsync runs - only one card, one Id, survives to be loaded. This check only
/// has something to find if the caller passes in cards merged from more than one store/run.
///
/// The UnmatchedModel check covers KBA matching: every model whose cards stayed
/// BundlePriority.Unknown must either be listed in kba-unlisted-models.json (FZ12 doesn't list it - see
/// <see cref="KbaUnlistedModelConfig"/>) or is reported, because then a model-aliases.json entry or a
/// BrandNames alias is missing (the shape of real bugs this found: Cupra matching nothing because KBA
/// counts it under "SEAT", a parser leaving a document id as the model name). The reverse - a listed
/// model that matched after all - is only a warning (UnlistedModelMatched): a new FZ12 edition may
/// have started listing it, so the entry should be reviewed. Both only run once <c>prioritize</c> has
/// assigned any priority at all, so a dataset that simply hasn't been prioritized yet doesn't look
/// like a bug. One issue per model, not per card, so a model with ten sheets isn't reported ten times.
/// </summary>
public static class DataQualityChecker
{
    private static readonly Regex YearOrYearRangePattern = new(
        @"^(19|20)\d{2}(-(19|20)\d{2})?$", RegexOptions.Compiled);
    private static readonly Regex BareDigitsPattern = new(@"^\d+$", RegexOptions.Compiled);

    public static IReadOnlyList<DataQualityIssue> CheckAll(
        IReadOnlyList<RescueCardMetadata> cards, KbaUnlistedModelConfig unlistedModels)
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
            // Combined multi-model documents carry no single model name (their split parts do), so
            // they say nothing about matching.
            foreach (var model in cards
                         .Where(c => c.DocumentScope != DocumentScope.Combined)
                         .GroupBy(c => (c.Brand, Model: ModelNameNormalizer.Normalize(c.ModelName ?? string.Empty))))
            {
                var first = model.First();
                var matched = model.Any(c => c.BundlePriority != BundlePriority.Unknown);
                var listed = unlistedModels.Contains(first.Brand, first.ModelName);

                if (!matched && !listed)
                {
                    issues.Add(new DataQualityIssue(first.Id, first.Brand, DataQualityIssueKind.UnmatchedModel,
                        Strings.Get("Quality_UnmatchedModel", model.Count(), first.ModelName ?? string.Empty)));
                }
                else if (matched && listed)
                {
                    issues.Add(new DataQualityIssue(first.Id, first.Brand, DataQualityIssueKind.UnlistedModelMatched,
                        Strings.Get("Quality_UnlistedModelMatched", first.ModelName ?? string.Empty, model.Count(c => c.BundlePriority != BundlePriority.Unknown)),
                        DataQualityIssueSeverity.Warning));
                }
            }
        }

        return issues;
    }
}
