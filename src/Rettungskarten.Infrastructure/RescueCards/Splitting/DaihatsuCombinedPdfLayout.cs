using System.Globalization;
using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;
using UglyToad.PdfPig.Content;
using PigPdfDocument = UglyToad.PdfPig.PdfDocument;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Layout of Daihatsu Deutschland's combined "daihatsu_rettungsdatenblaetter_de_at.pdf".
///
/// Neither of the two reusable layouts fits (verified against the real 25-page document): it has no
/// bookmarks, and the model pages are scanned images without any extractable text, so there is no
/// per-page header to key on. What it does have is an overview table on page 2 ("Übersicht
/// Rettungsdatenblätter") with the columns Modell | Ausführung | Amtlicher Typ | Bauzeitraum | Seite,
/// one row per sheet ("CUORE | L701 / 3-Türer | L7 | 1998-2003 | 10"); a row with an empty Modell
/// cell continues the model above it. That table is the layout: every row's "Seite" is where its sheet
/// starts, running until the next row's page.
///
/// PdfPig's plain <c>page.Text</c> glues the table cells together without separators
/// ("CUOREL701 / 3-TürerL71998-200310"), so rows and columns are rebuilt from word positions instead:
/// words on the same baseline form a row, and each word belongs to the column whose header starts at
/// or left of it (the column x positions are read from the header row itself, not hard-coded).
///
/// The "Amtlicher Typ" (type-approval designation, e.g. "L7", "M3") becomes the chassis code; the
/// "Ausführung" (model codes and door count) the variant.
/// </summary>
public sealed class DaihatsuCombinedPdfLayout : ICombinedPdfLayout
{
    private static readonly string[] HeaderWords = ["Modell", "Ausführung", "Amtlicher", "Bauzeitraum", "Seite"];
    private static readonly Regex PageNumber = new(@"^\d{1,3}$", RegexOptions.Compiled);
    private const double SameLineTolerance = 2.0;

    public Brand Brand => Brand.Daihatsu;

    public bool IsCombinedEntry(RescueCardMetadata entry) => entry.DocumentScope == DocumentScope.Combined;

    public IReadOnlyList<CombinedPdfPageGroup> DetectGroups(PigPdfDocument document)
    {
        var rows = ReadOverviewTable(document);

        var groups = new List<CombinedPdfPageGroup>();
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = rows
            .Where(r => r.Page >= 1 && r.Page <= document.NumberOfPages)
            .GroupBy(r => r.Page).Select(g => g.First()) // two rows claiming one page: keep the first
            .OrderBy(r => r.Page)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var row = ordered[i];
            var lastPage = i + 1 < ordered.Count ? ordered[i + 1].Page - 1 : document.NumberOfPages;
            var key = $"{row.Model} {row.Execution}".Trim();
            if (!usedKeys.Add(key))
            {
                key = $"{key}#p{row.Page}";
                usedKeys.Add(key);
            }

            groups.Add(new CombinedPdfPageGroup(key, Parse(row), Enumerable.Range(row.Page - 1, lastPage - row.Page + 1).ToList()));
        }

        return groups;
    }

    internal sealed record OverviewRow(string Model, string Execution, string OfficialType, string BuildPeriod, int Page);

    internal static IReadOnlyList<OverviewRow> ReadOverviewTable(PigPdfDocument document)
    {
        foreach (var page in document.GetPages())
        {
            var lines = GroupIntoLines(page.GetWords());
            var headerIndex = lines.FindIndex(l => HeaderWords.All(h => l.Any(w => w.Text == h)));
            if (headerIndex < 0)
            {
                continue;
            }

            var columnStarts = HeaderWords
                .Select(h => lines[headerIndex].First(w => w.Text == h).BoundingBox.Left)
                .ToArray();

            var rows = new List<OverviewRow>();
            string? currentModel = null;
            foreach (var line in lines.Skip(headerIndex + 1))
            {
                var cells = new List<string>[HeaderWords.Length];
                for (var c = 0; c < cells.Length; c++)
                {
                    cells[c] = [];
                }

                foreach (var word in line)
                {
                    var column = Array.FindLastIndex(columnStarts, x => x <= word.BoundingBox.Left + 1);
                    cells[Math.Max(column, 0)].Add(word.Text);
                }

                var pageCell = string.Join(' ', cells[4]);
                if (!PageNumber.IsMatch(pageCell))
                {
                    // The header's second line ("Typ") comes before the first row; the notes text
                    // after the table ends it.
                    if (rows.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                if (cells[0].Count > 0)
                {
                    currentModel = string.Join(' ', cells[0]);
                }

                rows.Add(new OverviewRow(
                    currentModel ?? string.Empty, string.Join(' ', cells[1]), string.Join(' ', cells[2]),
                    string.Join(' ', cells[3]), int.Parse(pageCell, CultureInfo.InvariantCulture)));
            }

            return rows;
        }

        return [];
    }

    internal static ParsedModelInfo Parse(OverviewRow row)
    {
        // "./." is the table's "none" marker: "G204./. 4-Türer" = model code G204, no further code.
        var execution = Regex.Replace(row.Execution.Replace("./.", " "), @"\s+", " ").Trim();
        var years = ModelYearRangeTextHelper.Extract(row.BuildPeriod, singleYearIsStartYear: true);
        // The table is all caps: "CUORE" -> "Cuore", but abbreviations stay ("YRV").
        var modelName = row.Model.Length == 0 ? null
            : row.Model.Length <= 3 ? row.Model
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(row.Model.ToLowerInvariant());

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: execution.Length == 0 ? null : execution,
            BodyType: null,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: VehicleAttributeTextHelper.ExtractDoors(execution),
            FuelType: null,
            LanguageCode: "DE",
            ParseConfidence: modelName is null ? ParseConfidence.Unparsed
                : years.From is not null ? ParseConfidence.High : ParseConfidence.Heuristic,
            ChassisCode: row.OfficialType.Length == 0 ? null : row.OfficialType);
    }

    private static List<List<Word>> GroupIntoLines(IEnumerable<Word> words)
    {
        var lines = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var line = lines.LastOrDefault();
            if (line is not null && Math.Abs(line[0].BoundingBox.Bottom - word.BoundingBox.Bottom) <= SameLineTolerance)
            {
                line.Add(word);
            }
            else
            {
                lines.Add([word]);
            }
        }

        foreach (var line in lines)
        {
            line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        }

        return lines;
    }
}
