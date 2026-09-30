using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Layout of Ford's combined "EU-rescue-cards-all-carlines_deDEU.pdf" (204 pages, no bookmarks -
/// verified, so page text it is): a cover, an imprint and three contents pages, then one card per
/// model variant. Each card's first page starts with its header, directly followed by the pictogram
/// legend: "FORD B-MAX08/2012 – > BLegende...", "FORD Focus - LPGTurnier08/2007 – 03/2011 FLegende..."
/// (PdfPig glues the header fields together; see <see cref="FordRescueCardParser.ParseCombinedHeader"/>).
/// Electrified models add several pages of high-voltage deactivation steps without that header -
/// those join the card before them. The header (without "FORD " and the legend) is the key; the 116
/// headers of the current document are all distinct, and a repeated one would start a new, suffixed
/// group rather than merge into a card of the same name further back.
/// </summary>
public sealed class FordCombinedPdfLayout : PageTextCombinedPdfLayout
{
    private static readonly Regex Header = new(@"^\s*FORD\s+(?<header>.{1,160}?)Legende", RegexOptions.Compiled | RegexOptions.Singleline);

    public override Brand Brand => Brand.Ford;

    protected override bool UnkeyedPagesContinuePreviousGroup => true;

    protected override bool MergeNonConsecutivePagesWithSameKey => false;

    protected override string? TryGetPageKey(string pageText)
    {
        var match = Header.Match(pageText);
        return match.Success ? CombinedPdfHeaderText.Collapse(match.Groups["header"].Value) : null;
    }

    protected override ParsedModelInfo ParseGroup(string key, IReadOnlyList<string> pageTexts) =>
        FordRescueCardParser.ParseCombinedHeader(key.Split('#')[0]);
}
