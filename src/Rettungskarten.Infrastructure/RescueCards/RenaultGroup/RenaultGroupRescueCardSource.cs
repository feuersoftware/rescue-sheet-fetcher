using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.RenaultGroup;

/// <summary>
/// Renault's and Dacia's German sites each have one static, server-rendered page listing every rescue
/// sheet as a direct PDF link on Renault Group's CDN (cdn.group.renault.com/ren/de/... and
/// /dac/de/...) - one class serves both brands, one instance per brand.
///
/// What the real pages look like (verified 2026-09-29):
/// - renault.de/tipps-und-anleitungen/rettungskarten.html: ~80 links in tab panels per model family
///   (PKW, vans, electric, hybrid), button text is the only label ("CAPTUR 2 - 2021", "ZOE E-TECH 2",
///   "MASTER E-TECH (ab 2019)"). The same PDF is linked from several tabs ("FLUENCE Z.E." three times,
///   "KANGOO RAPID" reuses Kangoo 1's file) - the base class de-duplicates by URL. The CDN URL is
///   "{folder}/{real file name}.pdf.asset.pdf/{10-hex asset id}.pdf", so the *last* segment the
///   base class would use as file name is only the asset id; the real name is the segment before it.
/// - dacia.de/rettungskarten.html: ~26 links with plain file URLs, button text "> Sandero 1" plus a
///   <c>title</c> ("Rettungsdatenblatt Sandero, 2008 bis 2012") that carries the years the button
///   text lacks; both go into the label for <see cref="RenaultGroupLabelParser"/>.
/// - both pages also link legal PDFs (warranty notice, connected-services terms) from the footer;
///   only links below a rescue-sheet folder ("/rettungskarten/", "/rettungsdatenblaetter/") count.
///
/// Every sheet is German. The persisted id uses the real file name (with its folder), not the asset
/// id, so an id survives Renault re-uploading an unchanged file.
/// </summary>
public sealed class RenaultGroupRescueCardSource : HtmlPdfLinkRescueCardSource
{
    private const string AssetSuffix = ".pdf.asset.pdf";

    private static readonly string[] SheetFolders = ["/rettungskarten/", "/rettungsdatenblaetter/"];

    private readonly Brand _brand;

    public RenaultGroupRescueCardSource(Brand brand, IHttpClientFactory httpClientFactory, ILogger<RenaultGroupRescueCardSource> logger)
        : base(httpClientFactory, logger)
    {
        if (brand is not (Brand.Renault or Brand.Dacia))
        {
            throw new ArgumentOutOfRangeException(nameof(brand), brand, "Only Renault and Dacia are served by this source.");
        }

        _brand = brand;
    }

    public override Brand Brand => _brand;

    protected override IReadOnlyList<string> PageUrls => _brand == Brand.Renault
        ? ["https://www.renault.de/tipps-und-anleitungen/rettungskarten.html"]
        : ["https://www.dacia.de/rettungskarten.html"];

    protected override IReadOnlyList<string> BrandPrefixes => [_brand.ToString()];

    protected override bool IsCandidateLink(string absoluteUrl, IElement anchor)
    {
        var path = new Uri(absoluteUrl).AbsolutePath;
        return path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) &&
            SheetFolders.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    protected override string GetLabel(IElement anchor)
    {
        var text = LabelText.Collapse(anchor.TextContent).TrimStart('>', ' ');
        var title = LabelText.Collapse(anchor.GetAttribute("title") ?? string.Empty);
        return title.Length == 0 || string.Equals(title, text, StringComparison.OrdinalIgnoreCase)
            ? text
            : $"{text} | {title}";
    }

    protected override bool IsExcluded(string label, string absoluteUrl) =>
        !Parsing.RescueDocumentClassifier.IsRescueSheet(label, RealFileName(absoluteUrl));

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) =>
        RenaultGroupLabelParser.Parse(label, RealFileName(absoluteUrl), BrandPrefixes);

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var entries = await base.DiscoverAsync(ct);

        // Swap the base class's raw name (the CDN asset id) for "folder/real file name"; should two
        // entries still collide, the full URL keeps ids unique.
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return entries
            .Select(e =>
            {
                var rawName = RealFileNameWithFolder(e.DownloadUrl!);
                return e with { RawFileNameOrLabel = used.Add(rawName) ? rawName : e.DownloadUrl! };
            })
            .ToList();
    }

    /// <summary>The file name a sheet was uploaded under: the segment before Renault's
    /// ".pdf.asset.pdf/{asset id}.pdf" suffix where present, otherwise the last segment.</summary>
    internal static string RealFileName(string url)
    {
        var segments = SegmentsOf(url);
        return segments.Length >= 2 && segments[^2].EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase)
            ? segments[^2][..^(AssetSuffix.Length - ".pdf".Length)]
            : segments[^1];
    }

    private static string RealFileNameWithFolder(string url)
    {
        var segments = SegmentsOf(url);
        var fileIndex = segments.Length >= 2 && segments[^2].EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase)
            ? segments.Length - 2
            : segments.Length - 1;
        var folder = fileIndex > 0 ? segments[fileIndex - 1] + "/" : string.Empty;
        return folder + RealFileName(url);
    }

    private static string[] SegmentsOf(string url) =>
        new Uri(url).AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
}
