# Rettungskarten & Vehicle Stock Tool

**Languages:** [🇬🇧 English](#english) | [🇩🇪 Deutsch](#deutsch)

---

## English

### What this is

A .NET 10 console application for German fire brigades that:

1. Downloads **rescue data sheets** ("Rettungskarten"/"Rettungsdatenblätter" — PDFs showing airbag locations, cut zones, and fuel/battery placement for a specific vehicle model) for **57 car brands** — every brand listed by rettungskarten-service.de and the ADAC for which the manufacturer still publishes rescue sheets itself (Volkswagen, Mercedes-Benz, BMW and Stellantis groups, Renault, Toyota, Hyundai/Kia, Ford, Nissan, Volvo, Tesla, ... — see the table below). Only the manufacturer's own source counts (or a portal/service provider the manufacturer itself links, e.g. Stellantis Servicebox, IFZ Berlin, Scene7); aggregators such as ADAC, rettungskarten-service.de or Euro Rescue are never used as a source.
2. Downloads the **KBA vehicle stock statistic (FZ12)** — how many vehicles of each model series are actually registered in Germany — and uses it to **prioritize** which rescue cards are common enough to bundle directly into a field app vs. only fetch on demand.
3. Stores everything in a predictable folder structure on disk, with a JSON metadata sidecar per rescue card (build year, model, manufacturer, sibling/platform-sharing models, estimated fleet size, bundle priority).

No manufacturer publishes a stable official API for this — every brand's page structure had to be reverse-engineered individually. See the per-brand status table below and the code comments in `src/Rettungskarten.Infrastructure/RescueCards/` for what was found and why some sources are more reliable than others.

### Supported brands

| Brand | Status | Source |
|---|---|---|
| VW | Fully functional | JSON feed on `assets.feature-app.io` |
| Audi | Fully functional | Static HTML page on `audi.com` |
| Škoda | Fully functional | Model pages on `skoda-auto.de` |
| SEAT | Fully functional | Model pages on `seat.de` |
| Cupra | Fully functional (Swiss site only; the Austrian site's downloads are gated behind an auth redirect) | Static HTML page on `cupraofficial.ch` |
| Porsche | Fully functional. Source is 2 combined PDFs (current + classic models); `fetch` downloads them as-is, then `split porsche` adds one file per model, same as every other brand | `porsche.com` official documents page (Vue SSR template embedded in a `<script type="text/x-template">` block) |
| Bentley | Fully functional | Static HTML page on `bentleymotors.com` |
| Lamborghini | Fully functional (English-language documents only; Lamborghini doesn't offer a separate German file) | Static HTML page on `lamborghini.com` |
| Mercedes-Benz | Fully functional | Static HTML overview on `rk.mb-qr.com` (the QR-sticker portal) + per-card detail pages for the PDF link |
| Mercedes-AMG | Fully functional | `rk.mb-qr.com` (same portal and overview as Mercedes-Benz) |
| Mercedes-EQ | Fully functional | `rk.mb-qr.com` (same portal and overview as Mercedes-Benz) |
| Mercedes-Maybach | Fully functional | `rk.mb-qr.com` (same portal and overview as Mercedes-Benz) |
| smart | Fully functional | fortwo/forfour/roadster (Mercedes-built, up to 2024) from `rk.mb-qr.com`; #1/#3/#5 (Mercedes/Geely joint venture, since 2022) from the app bundle of `rescuecard.smart.com` |
| BMW | Fully functional | JSON API of BMW Group's Aftersales Online System (`aos.bmwgroup.com`) |
| MINI | Fully functional | JSON API of BMW Group's Aftersales Online System (`aos.bmwgroup.com`) |
| Rolls-Royce | Fully functional | JSON API of BMW Group's Aftersales Online System (`aos.bmwgroup.com`) |
| Peugeot | Fully functional | Stellantis Servicebox static frameset on `public.servicebox-parts.com` (model pages) |
| Citroën | Fully functional | Stellantis Servicebox static frameset on `public.servicebox-parts.com` (model pages) |
| DS | Fully functional | Stellantis Servicebox static frameset on `public.servicebox-parts.com` (model pages) |
| Opel | Fully functional | IFZ Berlin JSON API (`ifz-berlin.de`, the "Stellantis Aftersales" rescue portal linked from opel.de) |
| Saab | Fully functional | IFZ Berlin JSON API (`ifz-berlin.de`) |
| Chevrolet | Fully functional | IFZ Berlin JSON API (`ifz-berlin.de`) |
| Cadillac | Fully functional | IFZ Berlin JSON API (`ifz-berlin.de`) |
| Fiat | Metadata only (robots.txt forbids PDF downloads; `--ignore-robots-txt` downloads them anyway, see [Ignoring robots.txt](#ignoring-robotstxt---ignore-robots-txt); `split fiat` then splits the older models' collection PDF) | Static HTML page on `fiat.de` (`/besitzer/rettungsdatenblaetter`) |
| Fiat Professional | Metadata only (robots.txt forbids PDF downloads; `--ignore-robots-txt` downloads them anyway, see [Ignoring robots.txt](#ignoring-robotstxt---ignore-robots-txt)) | Static HTML page on `fiat.de` (`/professional/besitzer/rettungsdatenblaetter`) |
| Jeep | Metadata only (robots.txt forbids PDF downloads; `--ignore-robots-txt` downloads them anyway, see [Ignoring robots.txt](#ignoring-robotstxt---ignore-robots-txt)) | Static HTML page on `jeep.de` (`/mopar/rettungsdatenblaetter`) |
| Alfa Romeo | Metadata only (robots.txt forbids PDF downloads; `--ignore-robots-txt` downloads them anyway, see [Ignoring robots.txt](#ignoring-robotstxt---ignore-robots-txt)) | Static HTML page on `alfaromeo.de` (`/rettungsdaten-blaetter`) |
| Lancia | Metadata only (robots.txt forbids PDF downloads; `--ignore-robots-txt` downloads them anyway, see [Ignoring robots.txt](#ignoring-robotstxt---ignore-robots-txt)) | Static HTML page on `lancia.de` (`/mopar/rettungsdatenblaetter`) |
| Abarth | Metadata only (robots.txt forbids PDF downloads; `--ignore-robots-txt` downloads them anyway, see [Ignoring robots.txt](#ignoring-robotstxt---ignore-robots-txt); `split abarth` then splits the older models' collection PDF) | Two static HTML pages on `abarth.de` (brand page + Mopar page) |
| Dodge | Fully functional (legacy models up to 2013 only) | Legacy static HTML page on `dodge.de` (http only) |
| Maserati | Fully functional (1 combined PDF from 2016, split with `split maserati`) | Aftersales page on `maserati.com/de/de` |
| Renault | Fully functional | Static HTML page on `renault.de` (PDFs on `cdn.group.renault.com`) |
| Dacia | Fully functional | Static HTML page on `dacia.de` (PDFs on `cdn.group.renault.com`) |
| Toyota | Fully functional | Static HTML page on `toyota.de` (PDFs on Toyota Europe's Scene7 store) |
| Lexus | Fully functional | Static HTML page on `lexus.de` (PDFs on Toyota Europe's Scene7 store) |
| Daihatsu | Fully functional (1 combined PDF, split with `split daihatsu`) | Combined PDF linked from the `daihatsu.de` homepage |
| Hyundai | Fully functional. Current models per model; models before 11/2019 only as 1 combined PDF, `split hyundai` adds one file per model. "Maßnahmen im Notfall" guides (ERGs) are not collected | Static HTML page on `hyundai.com/de` (download list; PDFs on Adobe Scene7 without `.pdf` extension) |
| Kia | Fully functional. Some sheets exist in English only (kept as `EN`); older models (up to 08/2020) only as 1 combined PDF, `split kia` adds one file per model | Static HTML page on `kia.com/de` |
| Nissan | Fully functional. Older models (up to 06/2019) additionally as 1 combined PDF, `split nissan` adds one file per model; ERGs ("Rettungsleitfaden") are not collected | Static HTML tables on `nissan.de/rescuers_page.html` |
| Ford | Fully functional. Models up to ~2020 only in 1 combined PDF ("EU-rescue-cards-all-carlines", 204 pages), `split ford` adds one file per model; newer models per model | Vehicle lookup on Ford's service portal `fordserviceinfo.com` (linked from ford.de), PDFs on `fordservicecontent.com` |
| Mazda | Fully functional (a few sheets English only - Mazda has no German edition of them) | `mazda.de/rettungskarten` - content blocks embedded as JSON in the page (`window.mxp.data.push(JSON.parse('…'))`), rescue cards in the "ALLE RETTUNGSKARTEN" FAQ block |
| Honda | Fully functional | Static HTML page on `honda.de` (model heading, link text and "Amtlicher Typ / Bauzeitraum" paragraph per sheet) |
| Volvo | Fully functional | Static HTML page on `volvocars.com` (Akamai - browser client) |
| Jaguar | Fully functional | Static HTML page on `jaguar.com/de-de` |
| Land Rover | Fully functional | Static HTML page on `landrover.de` (model in the links' `aria-label`) |
| Tesla | Fully functional (several sheets English only - Model Y, newer Model S/X, Roadster) | First-responder page `tesla.com/de_DE/firstresponders/vehicles-charging` (Akamai - browser client), PDFs on `digitalassets.tesla.com` |
| MG | Fully functional (no build years published; the MG4 MY23 sheet is English only) | Static HTML page on `mgmotor.de` |
| KGM (SsangYong) | Fully functional (the newer sheets - Torres, Actyon, Korando e-Motion, Musso EV/Q300 - are English only) | Static HTML table on `kgm.de` |
| Mitsubishi | Fully functional (current European range: Space Star, Colt, ASX, Eclipse Cross, Outlander, L200; older generations sold in Germany are not covered) | Rescue-card page of Mitsubishi Austria on `mitsubishi-motors.at` (one card per sheet: heading + PDF link) |
| Subaru | Fully functional (1 combined PDF for the whole range, split with `split subaru`) | Safety page on `subaru.de` (HubSpot CMS) |
| Suzuki | Fully functional | Document pages on `auto.suzuki.de`: overview → 4 category pages (model tiles only in the Next.js RSC payload) → per-model pages |
| BYD | Fully functional (Austrian importer's site; BYD has no German page with rescue sheets) | Downloads page on `bydauto.at` (Webflow CMS) |
| Isuzu | Fully functional | Static HTML page on `isuzu-sales.de` |
| MAXUS | Fully functional | Static HTML page on `maxus.de` (Storyblok assets) |
| RUF | Fully functional | Static HTML page on `ruf-automobile.de` |
| StreetScooter | Fully functional | Customer support page on `streetscooter.com` |
| Polestar | Not implemented: Polestar publishes its rescue sheets only as individual PDFs on `polestar.com/dato-assets/…` that no page links | – |

Run `list brands` any time for the current status (it also shows each brand's corporate group).

**Not available** (no official source left — these brands have no `Brand` value): Chrysler (domain parked; Thema and Voyager are covered by Lancia), Lada (domain dead), Think City (manufacturer dissolved), Infiniti (domain dead, left Europe in 2020), Datsun (never sold in Germany), e.GO (insolvent, pages return 404), Aiways (domain parked, only a Swiss importer left), Lynk & Co (no rescue page on lynkco.com). Brands in the KBA stock that neither overview lists — Alpine, Ineos, Leapmotor, Xpeng, GWM/ORA, Aston Martin, Ferrari, Lotus, Iveco, MAN — are candidates for a later wave.

**Possible extension: motorhomes** (researched, not implemented — KBA counts motorhomes only as "WOHNMOBILE ZUSAMMEN" or under their base vehicle, e.g. FIAT DUCATO, so no per-manufacturer fleet match is possible): Dethleffs (`dethleffs.de/service/rettungskarten`, ~922 sheets, path `/…/<model year>/<model>-<a|i|t>-<floor plan>.pdf`), Sunlight (`sunlight.de/infomaterial/#rettungskarten`, one Dropbox folder per model year, `dl=1` returns a ZIP, ~150–190 sheets), CS-Reisemobile (`cs-reisemobile.de/…/rettungskarten.htm`, 31 sheets, the number in the name is the Sprinter series 901–907).

### Quick start

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build
dotnet run --project src/Rettungskarten.Cli -- list brands
```

### CLI usage

```bash
# Download rescue cards for one brand (or "all")
dotnet run --project src/Rettungskarten.Cli -- fetch rescue-cards --brand skoda --output ./data/rescue-cards

# Discover + parse only, skip PDF downloads
dotnet run --project src/Rettungskarten.Cli -- fetch rescue-cards --brand audi --dry-run

# All brands except some (space-separated)
dotnet run --project src/Rettungskarten.Cli -- fetch rescue-cards --brand all --exclude-brands volvo tesla

# Ignore robots.txt for the named brands only - read "Ignoring robots.txt" below first
dotnet run --project src/Rettungskarten.Cli -- fetch rescue-cards --brand fiat --ignore-robots-txt fiat

# Download the KBA vehicle stock (FZ12) for a reference year
dotnet run --project src/Rettungskarten.Cli -- fetch stock --year 2026

# Cross-reference rescue cards against the stock data -> bundle priority
dotnet run --project src/Rettungskarten.Cli -- prioritize

# Split a brand's combined all-models PDF(s) (already fetched above) into one file per model
dotnet run --project src/Rettungskarten.Cli -- split porsche
dotnet run --project src/Rettungskarten.Cli -- split all

# List brand implementation status
dotnet run --project src/Rettungskarten.Cli -- list brands

# Developer tool: inspect an XLSX file's schema
dotnet run --project src/Rettungskarten.Cli -- inspect xlsx path/to/file.xlsx

# Check already-fetched rescue-card metadata for anomalies (see "Data quality checks" below)
dotnet run --project src/Rettungskarten.Cli -- inspect quality --rescue-cards-path ./data/rescue-cards
```

Every command supports `--verbose` for detailed logs.

### Ignoring robots.txt (`--ignore-robots-txt`)

> **Disclaimer:** `--ignore-robots-txt` makes the tool download files that the site's `robots.txt` asks automated clients not to fetch. `robots.txt` is the site owner's stated wish, not an access control: ignoring it may breach the site's terms of use, and the owner may block the tool. **You alone are responsible for whether this use is permitted** — if in doubt, ask the manufacturer for permission. The authors accept no liability. Use the downloaded files only for their purpose: rescuing people from vehicles.

By default every request honours the target host's `robots.txt` (see "Known limitations"). `--ignore-robots-txt <brand...>` lifts that for the named brands only — there is no "all": every other brand in the same run keeps honouring `robots.txt`, even on a shared host. Everything else stays as it is: the per-host rate limit, timeouts and redirect handling. The disclaimer above is printed on every run that uses the option; a named brand that isn't part of the run (not selected by `--brand`, or excluded) is ignored with a warning.

It exists for the Fiat, Fiat Professional, Jeep, Alfa Romeo, Lancia and Abarth sites, whose `robots.txt` forbids `*.pdf` — without it those brands are metadata only:

```bash
dotnet run --project src/Rettungskarten.Cli -- fetch rescue-cards --brand all --ignore-robots-txt fiat fiatprofessional jeep alfaromeo lancia abarth
```

### Language / localization

All of the app's own console output (command help, status tables, error messages) is localized in **German and English**. Select it explicitly with `--lang de` / `--lang en`; without it, the app follows the OS/environment language and falls back to English for anything else.

```bash
dotnet run --project src/Rettungskarten.Cli -- --lang en list brands
```

Translations live in `src/Rettungskarten.Core/Localization/Strings.resx` (English, the neutral/fallback resource) and `Strings.de.resx` (German). Add a new language by adding a `Strings.<culture>.resx` with the same keys — no code changes needed beyond extending `Strings.ParseLanguageOption` and the CLI's `--lang` value list.

### On-disk output layout

```
data/
  rescue-cards/
    <brand>/<model>/<id>.json   (+ .pdf if the download succeeded)
    <brand>/_manifest.json      (derived, aggregates that brand's sidecars; rewritten by fetch, split and prioritize)
  stock/
    <year>/fz12_<year>.xlsx     (raw download)
    <year>/fz12_<year>.json     (parsed)
    <year>/fz12_<year>.meta.json
  priority-report.json          (one row per model, sorted by estimated fleet size)
  priority-report.csv           (same data, for Excel/spreadsheet use)
```

`<brand>` is the lower-cased brand name as used by `--brand` (e.g. `mercedesbenz`, `landrover`). Besides the parsed model data, every sidecar records:

- `manufacturerGroup` — the corporate group (e.g. `volkswagenGroup`, `stellantis`, `independent`); also a column of `priority-report.json`/`.csv`. smart is the one brand whose group depends on the source (Mercedes-Benz up to 2021, Geely from 2022).
- `documentScope` — `single` (one model per file), `combined` (one file covering many models, e.g. Porsche's all-models PDF) or `splitPart` (cut out of a combined file by `split`, with `splitSourceId` pointing at the combined entry, which is kept). `combined` entries are not rows of `priority-report.json`/`.csv` — their split parts are.
- `chassisCode` — the manufacturer's series/platform code where the source states it (e.g. `W177`, `F45`), otherwise `null`.
- `fuelType` — normalized across all sources to one vocabulary: `Petrol`, `Diesel`, `Petrol/Diesel` (one sheet for both), `Electric`, `Hybrid`, `Mild Hybrid`, `Plug-in Hybrid`, `Hydrogen`, `CNG`, `LPG`, `Ethanol` (`Rettungskarten.Core/Models/FuelTypes.cs`). A source's wording outside it (e.g. Mercedes' "Hybrid Benzin") is kept as written; `null` where the source doesn't say.

Sidecars written before these fields existed are read with the defaults (`single`, the brand's default group, no chassis code).

### For developers

**Solution layout** (4 projects, `Rettungskarten.slnx`):

- `Rettungskarten.Core` — domain models, interfaces (`IRescueCardSource`, `IVehicleStockSource`, ...), orchestration, priority-matching logic, and the shared localization strings. No I/O.
- `Rettungskarten.Infrastructure` — HTTP client setup (with per-host rate limiting), AngleSharp-based HTML parsing, ClosedXML-based XLSX parsing, the brand sources (one folder per multi-brand portal, e.g. `RescueCards/Mercedes/`, `RescueCards/Stellantis/`), the combined-PDF splitter and its per-brand layouts, the KBA stock source, and file-system storage.
- `Rettungskarten.Cli` — `System.CommandLine`-based entry point and command implementations.
- `Rettungskarten.Tests` — xUnit tests, including a regression suite that runs the KBA XLSX parser against a real downloaded file (`tests/Rettungskarten.Tests/Fixtures/fz12_2026.xlsx`). Every brand source (and the KBA stock source) has a `DiscoverAsync`/`FetchWithRawAsync`-level test against a stub `IHttpClientFactory` and a real, trimmed HTML/XLSX fixture — not just the shared parser classes' own unit tests — since the shared parsers alone wouldn't have caught e.g. the VW language-bucket-path bug or the Audi filename-parser field-shift bug found this session.

**Adding a new brand**: add its `Brand` enum value (that alone makes it a valid `--brand`/`split` argument and part of `--brand all`), map it in `BrandGroups` (group, and parent brand if KBA counts its vehicles under another brand's name) and, if KBA spells it differently, in `BrandNames` (if FZ12 has no row for it at all, list it as `"*"` in `kba-unlisted-models.json` instead); implement its `IRescueCardSource` in `Rettungskarten.Infrastructure/RescueCards/` and register it in `Rettungskarten.Cli/CompositionRoot.cs` (a brand may have several sources — every one is discovered, each entry downloads through the source that found it); add `Brand_<Brand>_Source` (and, if coverage differs from the per-model German norm, `Brand_<Brand>_Status`) to both resource files for `list brands`. Most sources derive from shared building blocks: `HtmlPdfLinkRescueCardSource` (static page(s) with direct PDF links), `StandardRescueSheetFilenameParser` (the industry-wide `Brand_Model_Body_Year_Doors_Fuel_LANG.pdf` convention), `RescueSheetLabelParser` (free-text labels), `RescueDocumentClassifier` (drops ERGs, legends, manuals) and `LanguagePreference` (German, English only as a fallback). `DiscoverAsync` must never throw for a single model's parsing trouble; `DownloadAsync` must return a failed `RescueCardDownloadResult` rather than throwing for expected HTTP failures — the orchestrator only treats an exception from `DiscoverAsync` as a brand-level failure. Sources whose real PDF URL is short-lived or one hop away override `RescueCardSourceBase.ResolveDownloadUrlAsync` instead of resolving it during discovery.

**Splitting a combined multi-model PDF**: `split <brand|all>` cuts a brand's combined "all models" PDFs into one file per model. The brand-independent part (reading with `PdfPig`, copying page ranges into standalone PDFs with `PDFsharp`) is `Rettungskarten.Infrastructure/RescueCards/Splitting/CombinedPdfSplitter.cs`; how one brand's document maps pages to models is an `ICombinedPdfLayout` registered in `CombinedPdfLayouts.cs`. `PageTextCombinedPdfLayout` is the reusable base for the usual case, a model recognizable from each page's own text (Porsche's layout groups pages by the document's own "ID no." footer); none of the combined PDFs found so far has usable bookmarks, so there is no bookmark-based base. The combined entry is kept (`documentScope: combined`); each part becomes a `splitPart` entry, and re-running `split` replaces a document's previous parts (the new parts are saved first, then the old ones removed). `split` lists the pages it appended to a model without that page's own header and the pages that ended up in no part — both are also what a model whose header stopped matching looks like, so check them after a manufacturer publishes a new edition — and exits non-zero if any document couldn't be split (PDF missing on disk, unreadable, no models detected), after still splitting the others. It's a separate command rather than part of `fetch`, so re-running it doesn't re-download the large source file — the same reasoning `prioritize` already follows as its own post-processing step.

**Data quality checks**: `inspect quality --rescue-cards-path <path>` (`Rettungskarten.Core/Quality/DataQualityChecker.cs`) checks already-fetched metadata for anomalies that a "did discovery return results" check can't catch — a `bodyType` that's actually a year (the shape of a real bug once found in Audi's parsing: a shifted field silently corrupted `BodyType`/`BuildYearFrom` for several cards), a `fuelType` that's just digits, a duplicate `id`, or a model stuck at `bundlePriority: unknown` that isn't listed in `Config/kba-unlisted-models.json` (the shape of real bugs: every Cupra card unmatched because KBA counts Cupra under SEAT, or a split part named by its document id). That file lists the models FZ12 doesn't list as their own series — too rare, too old or too new, not a passenger car, or a special deliberately not aliased — each with a reason; a listed model that matches after all is reported as a warning (exit code stays 0) so the entry can be reviewed. These two checks only mean anything once `prioritize` has actually run, so they won't fire from `link-check.yml`'s dry-run-only flow, only when run locally after the full `fetch` → `prioritize` pipeline. A newly unmatched model needs either a `model-aliases.json` or a `kba-unlisted-models.json` entry — both are maintained by hand; there's deliberately no fuzzy rule. Since discovery already populates `bodyType`/`fuelType` even in `--dry-run` mode, the other checks run straight against dry-run output with no extra downloads — see the `link-check.yml` step below.

**Continuous integration** (`.github/workflows/`):

- `ci.yml` — build, test, and a `list brands` smoke test under both `en-US` and `de-DE` locales, on every push and PR.
- `link-check.yml` — a scheduled (weekly, Mondays) discovery-only run against every live source reachable from GitHub's runners (`fetch rescue-cards --brand all --dry-run --exclude-brands …` plus `fetch stock`; the excluded brands reject GitHub's runners, see "Known limitations" — an informational step tries each of them and warns when one is reachable again, so it can come off the workflow's `EXCLUDED_BRANDS`), followed by an `inspect quality` pass over the dry-run's output, so a manufacturer/KBA site change *or* a parsing regression is caught within a week instead of silently rotting; also runnable on demand from the Actions tab. See `CLAUDE.md` for what to do when adding a source that this workflow should also cover.

**Running tests**:

```bash
dotnet test
```

See `CLAUDE.md` for the project's contribution rules (English-only code/commits/PRs, build+test+review before committing, README maintenance).

### Known limitations

- **Porsche**: unlike every other brand, the source has no per-model file — Porsche publishes one combined PDF covering all current models (~55MB) plus a second for classic models, both served from Porsche's own CDN (`files.porsche.com`). `fetch rescue-cards --brand porsche` downloads those two files as-is (needing longer HTTP timeouts than the other brands, configured in `HttpServiceCollectionExtensions`); running `split porsche` afterwards splits them into one file per model by detecting model boundaries in the page text (grouped by the document's own "ID no." footer, since there's no PDF outline) and adds the per-model entries next to the two combined ones (which are kept, marked `documentScope: combined`). The document's actual language is English, not German (Porsche doesn't offer a separate German file here), and its content is in English (`languageCode: "EN"` on these entries, unlike every other brand). Model names/years are parsed heuristically from free text. The few sheets that carry no header at all (E-Hybrid supplements, the 911 Speedster and the 992 Cabriolet in the 2025 edition) are named by hand by their document ID in `Config/porsche-headerless-sheets.json` (edit it if Porsche renumbers or adds such a sheet - a sheet that has a header always uses the header instead); `PorscheCombinedPdfLayout` also fixes one page whose footer carries a wrong ID; `split` reports every multi-page sheet whose pages aren't exactly the ones its footer numbers ("Page x of y"), so a missing or foreign page - e.g. from a misprinted ID - doesn't go unnoticed; an unknown headerless sheet still falls back to its ID as the "model name" (`parseConfidence: "unparsed"`) rather than being dropped. The sheets in the newer Euro NCAP layout at the end of the 2025 edition are grouped by their "WP0_…" ID, or by their header where the ID is only a placeholder ("GB-?", Panamera G3). `bodyType` is also extracted from this free text (e.g. "Cabriolet", "SUV"); `doors`/`fuelType` are not, since Porsche's header text never states a door count and folds any fuel/drivetrain info (e.g. "E-Hybrid") into the model name itself rather than stating it separately.
- **robots.txt is respected**: every request is checked against the target host's `robots.txt` (RFC 9309 rules for this tool's product token `RettungskartenTool`, otherwise `*`) by `RobotsTxtDelegatingHandler`. A disallowed download is skipped with the failure reason "disallowed by the host's robots.txt" and the card is kept as metadata only — this affects the Stellantis brand sites that publish `Disallow: *.pdf$` (e.g. fiat.de) unless they're named in `--ignore-robots-txt` (see the disclaimer under "Ignoring robots.txt"). A robots.txt that can't be fetched (4xx) means no restrictions; a 5xx/timeout/network error is logged and treated the same for the requests waiting on it, but isn't cached — the next request to that host fetches it again, so a transient outage neither drops cards nor switches off a host's rules for the rest of the run. Redirects are followed by the tool itself rather than the HTTP stack, so every hop (e.g. BMW's signed S3 links) is checked against its own host's robots.txt and rate limit like a request of its own.
- **Bot protection (Akamai)**: several manufacturer sites (the Stellantis brand sites, Volvo, Tesla, Bentley, Ford's content CDN) answer non-browser requests with 403. Their sources use a separate `rettungskarten-browser` HTTP client that sends a browser's header set (User-Agent, `Accept-Language`, `Sec-Fetch-*`) and decompresses responses; robots.txt still applies to it. This only works **on Windows from a normal network**. Tested 2026-09-30/10-06 with identical code: the Akamai sites (Bentley, Fiat, Fiat Professional, Abarth, Alfa Romeo, Lancia, Jeep, Maserati, Volvo, Tesla) answer 403 to the same client on Linux even from the same network and public IP (WSL2, Ubuntu 24.04) - Akamai recognizes the Linux TLS stack, not just the headers - and they also block GitHub-hosted runners on Windows. Suzuki only blocks GitHub's runners (its plain client works from Linux on a normal network). Imitating a browser's TLS handshake to get past this was deliberately not attempted. So run these brands on Windows; `link-check.yml` skips all eleven with `--exclude-brands` - check them with a local dry run instead (`fetch rescue-cards --brand <brand> --dry-run`), e.g. before a release, and remove a brand from the workflow's list once a run shows it reachable again.
- **Cupra**: only the Swiss site is wired up; the Austrian site's downloads redirect to an identity/auth gateway.
- **Bentley**: each model's per-language PDF links use a *different* URL naming scheme depending on when that model's sheet was published (plain, spelled-out-language, `_Web` suffix, dated suffix, ...) - there's no single URL shape that reliably means "German". `BentleyRescueCardSource` filters on the link's own visible button label ("DEUTSCHE") instead, which is consistent across every scheme found.
- **Lamborghini**: like Porsche, the documents are English only - Lamborghini doesn't publish a separate German file.
- Rescue card filenames/link text are parsed heuristically (no brand publishes structured metadata) — see `ParseConfidence` on each entry. For VW/SEAT/Cupra this is the shared `StandardRescueSheetFilenameParser`; Audi gets its own `AudiFilenameParser` instead, since its CMS occasionally emits filenames the shared parser can't handle correctly (see that class's doc comment). Škoda's `bodyType`/`doors`/`fuelType` are likewise extracted from its model-page titles where stated (e.g. "Fabia Combi", "Citigo 3-Türer", "Octavia CNG") — only ~30% of titles state these explicitly, so most entries correctly leave them `null` rather than guessing.
- KBA's FZ12 file only lists model series with ≥1,000 registered vehicles (their own publication threshold); rarer models fall back to `bundlePriority: unknown`, which is the correct "fetch on demand" signal for this tool's purpose. FZ12 also only counts passenger cars: pickups and vans registered as trucks (Hilux, Amarok, Navara, NV400, ...) and quadricycles (Ami, Rocks-e, Twizy) aren't in it at all. Both kinds are listed in `Config/kba-unlisted-models.json` (see "Data quality checks"). The priority report groups model names the way they're matched (case and punctuation ignored), so "NAVARA" and "Navara" are one row.
- FZ12 has no finer breakdown than "model series" - no split by generation, body type, or fuel type (verified against a real download: only a `Segment`/`Modellreihe`/`Anzahl` column layout, no others). So every rescue-card variant of one model (e.g. every Golf model year/body style) gets the *same* `estimatedFleetSize`/`bundlePriority` - there's no more granular KBA source to improve this with. `priority-report.json`/`.csv` accounts for this by listing one row per (brand, model) with a `cardCount` of how many rescue-card variants exist (and a `notDownloadedCount` of how many of those don't have a local PDF yet, so a "High priority" model with some failed downloads doesn't look fully covered), rather than repeating the same model with the same numbers once per card.
- KBA's FZ12 does not track Cupra as its own brand at all — every Cupra model is counted under "SEAT" instead (`BrandGroups.ParentBrandOf` makes SEAT Cupra's parent brand, so `BrandNames` matches Cupra cards against SEAT-labelled stock rows; the same mechanism covers Mercedes-AMG/-EQ/Maybach → MERCEDES, and Fiat Professional/Abarth → FIAT). This means a Cupra card's `estimatedFleetSize` is the combined SEAT+Cupra registration count for that model name, not a Cupra-only figure — the best available approximation given KBA's granularity, not an exact count.
- Porsche's rescue cards use fine-grained per-generation/variant names (e.g. "911 Carrera", "Cayenne E-Hybrid") that don't match KBA's much coarser 9 tracked Porsche series (911, Boxster, Cayman, Cayenne, Macan, Panamera, Taycan, 928, 968) without help — `model-aliases.json` maps the known variants to their base KBA series. Genuinely untracked classics (356, 924, 944, 959, ...) correctly stay `bundlePriority: unknown`. Deliberately *not* aliased: ultra-low-volume specials (911 GT2/GT2 RS/GT3/GT3 RS/R/Turbo/Speedster, 911 G-Model Turbo, 718 Cayman GT4, Boxster Spyder; listed in `kba-unlisted-models.json`) — mapping these to the base series' aggregate fleet count would overstate their real-world commonality far more than it would for an ordinary trim, so `unknown` (the honest "fetch on demand" signal) is more correct here than a falsely inflated priority.
- All JSON files (rescue-card sidecars, the KBA stock result/meta files) are written atomically (temp file + rename), so a process kill mid-write can't leave a truncated file behind. If a sidecar or stock file is nonetheless unreadable (e.g. external interference, or one written before this fix), it's skipped with a warning printed to stderr rather than aborting the whole command — for a rescue-card sidecar, only that one card is missing from the run's results until it's re-fetched or removed; for the single KBA stock result file, the whole `prioritize` run falls back to its usual "no stock data found" message, since there's nothing to partially recover from one file.
- **Mercedes-Benz / AMG / EQ / Maybach / smart (rk.mb-qr.com)**: one portal serves all five brands; the ~1.4MB overview is fetched once per run and shared. The PDF link is only on each card's detail page, so `fetch` without `--dry-run` makes two requests per card (detail page + PDF) - discovery itself stays one request. Door counts are not stated in the overview and stay `null`. Model names are the portal's class names ("A-Klasse", "GLC", "EQA"); the engine designation goes into `variant`, the type number (e.g. "W177") into `chassisCode`.
- **Mercedes-EQ / KBA**: FZ12 has no EQ model series at all, although every EQ model is far above the 1,000-vehicle threshold - KBA evidently counts them under the related combustion series. `model-aliases.json` maps EQA -> GLA, EQB -> GLB, EQC -> GLK/GLC, EQE -> E-Klasse, EQS -> S-Klasse, EQV -> V-Klasse; the resulting fleet size is that of the whole series, an approximation. The same applies to T-Klasse/EQT/eCitan (-> Citan), eVito (-> Vito), eSprinter (-> Sprinter) and Marco Polo (-> V-Klasse).
- **Maybach / AMG specials**: the 2002-2013 Maybach 57/62, Mercedes-AMG ONE, AMG PureSpeed and SLR McLaren are deliberately not aliased (KBA doesn't list them; mapping them to a base series would inflate their priority), so they stay `bundlePriority: unknown`.
- **smart #1/#3/#5**: rescuecard.smart.com is an Angular app without an API; the model list is read from its compiled JavaScript bundle. If smart rebuilds the app so that the catalogue's shape changes, discovery fails loudly (link check) rather than silently returning nothing. Their `manufacturerGroup` is `geely`; the older smart generations keep `mercedesBenzGroup`.
- **BMW / MINI / Rolls-Royce**: all three come from one public JSON API (`aos.bmwgroup.com/api/v2/rescue-sheets`, German list only - no model there lacks a German sheet). The PDF link it hands out is a pre-signed S3 URL valid for 2 hours, so the stored `downloadUrl` is the portal's stable download endpoint, which redirects to a fresh signed URL on every download. Body types come from the portal's own category, which is too coarse and occasionally wrong (MINI's new 5-door Cooper F65 filed as "Clubman", the Cullinan as "sedan", MINI's "coupe" category meaning its 3-door hatch); a small chassis-code table in `BmwGroupRescueSheetParser` corrects the known cases. Electric i-models are named as such (`i4`, `i5`, `i7`, `iX3`) and mapped to the KBA series they are registered under (4ER, 5ER, 7ER, X3) via `model-aliases.json`. KBA lists every MINI model as a single "MINI" series, so all MINI cards share one fleet figure; Rolls-Royce isn't listed in FZ12 at all (`bundlePriority: unknown`, expected).
- **Stellantis Servicebox (Peugeot, Citroën, DS)**: the model pages mix two table layouts and at least five PDF naming schemes, and put Emergency Response Guides in the same pages - the ERG section is recognized by its title ("Handbuch zur Rettung (ERG)"), because some ERG files are not named as such. Metadata is parsed from the link/heading labels, not the filenames. Older rows use unnamed fuel pictograms, so their fuel type stays empty unless the label names it. The documented host `public.servicebox.com` no longer resolves; `public.servicebox-parts.com` serves the same pages.
- **IFZ Berlin (Opel, Saab, Chevrolet, Cadillac)**: the API only answers with the portal page as `Referer` and has no "all models" query, so discovery makes one request per model group (~75 for Opel, about 1.5 minutes at the default rate limit). Vauxhall is deliberately not a brand of its own: IFZ's Opel collection is labelled "Opel/Vauxhall" and has no Vauxhall-specific models - its English version is the same vehicles translated for the UK market, and KBA counts them as Opel.
- **Fiat, Fiat Professional, Jeep, Alfa Romeo, Lancia, Abarth**: the German brand sites forbid `*.pdf` in their robots.txt, so these sources only record metadata (model, fuel type, doors, source/download URL) - no PDF is downloaded, unless the brand is named in `--ignore-robots-txt` (read the disclaimer first). Most of their link labels state no build year ("Jeep® Compass e-Hybrid", "Fiat Tipo Kombi"), so the year fields stay empty for them; the dates inside Stellantis document codes (`..._DE_01_06_23_T`) are release dates, not build years, and are deliberately ignored.
- **Fiat / Abarth "ShedaSoccorso" collections**: fiat.de (also linked from the Fiat Professional page) and the Abarth Mopar page each link one brand-wide collection PDF of older models (Fiat: 43 sheets, Abarth: 4, from 2010-2012). Each is kept as a `Combined` entry ("Various Models") and is only downloaded with `--ignore-robots-txt` (robots.txt forbids it). `split fiat`/`split abarth` then add one card per sheet: the sheets print their model only as an image, so the models come from the document's table of contents ("FIAT GRANDE PUNTO LPG 3" = 3 doors, LPG), matched to each sheet by the sheet number in its footer; build years come from the sheet's own "(AB_01/2010)"/"(BIS_12/2009)" marker, since the table drops the "ab"/"bis". Fiat Professional's copy of the Fiat collection is not split - its sheets are Fiat passenger cars.
- **Lancia**: the page labels two different Ypsilon LPG sheets identically ("Lancia Ypsilon 2011 LPG"), so Lancia sheets are parsed from their (consistent) filenames instead of the labels.
- **Dodge**: only the legacy `dodge.de` site (Dodge left the German market in 2011) still carries German sheets - six sheets for Caliber, Journey and Nitro, over plain http (the host has no https).
- **Maserati**: Maserati Germany only publishes one combined PDF (05/2016, six models on one page each). `split maserati` turns it into GranCabrio, GranTurismo, Ghibli, Levante and two Quattroporte cards; newer models (Grecale, MC20, the 2023 GranTurismo) are not covered by it.
- **Renault/Dacia:** 57 of the 74 Renault sheets (Clio 1-4, Laguna, Scénic 1-3, most E-Tech sheets, ...) are published under file names without vehicle data or whose only year is the publication date of the sheet (2014), and their labels only give the generation or trim ("CLIO 3", "ZOE E-TECH 2") - these cards have no build years. Dacia's labels always state the years.
- **Toyota/Lexus:** the page lists no drivetrain for petrol/diesel models, so only hybrid, plug-in, electric and hydrogen sheets carry a fuel type. Lexus lists the RX 350h/450h+ twice (left- and right-hand-drive sheets); both are kept.
- **Daihatsu:** Daihatsu left Europe in 2013; the one combined PDF (23 sheets up to 2011) is scanned images without text or bookmarks, so `split daihatsu` reads the model boundaries from the document's overview table on page 2.
- **Ford discovery is slow by design**: Ford publishes its cards only behind a year + vehicle-line lookup on `fordserviceinfo.com`, and cards are attached per model year. Discovery therefore performs one lookup per European vehicle line and model year from 2019 on (~210 requests, ~4-5 minutes at the 1 request/second politeness limit). Lookups for earlier model years only return the combined PDF, so they are skipped. The German market is selected by sending the `UserCountry` cookie the portal's country form sets; if Ford changes that, discovery fails with a clear error (and the weekly link check turns red).
- **Hyundai/Kia/Nissan/Ford combined PDFs** have no bookmarks; `split` finds model boundaries from each sheet's printed header. Sheets whose header states no build years (a few Ford cards, e.g. "Transit Courier", "Explorer PHEV") are split correctly but marked as unparsed.
- **Kia**: a few older files carry no year in their name ("Kia-EV6-Rettungsdienste.pdf", "Kia-Sportage-PHEV.pdf"); those cards have no build year.
- **Mazda / Tesla**: some models only have an English rescue sheet (Mazda: Mazda3 and CX-30 from 2023, CX-60 e-Skyactiv D; Tesla: Model S/X 2021 and 2022+, every Model Y sheet, Roadster). These are kept with `languageCode: "EN"`; wherever a German sheet exists for the same generation, only the German one is kept. The language is taken from the filename (Mazda's `rsde`/`rsen` document codes, Tesla's `_de`/`_en` suffix), not from the German link labels, which Tesla uses for English files too.
- **Mazda**: the page lists VIN ranges for each sheet (e.g. `JMZKE******100000`); they are not part of the metadata schema and are dropped. The Mazda generation code (KE, KF, BP, ...) is kept as `chassisCode`.
- **Jaguar**: the live page links "XK Cabriolet (2005-2014)" to the XK *Coupé*'s PDF. `JaguarRescueCardSource` skips a link whose label and filename name different body styles (with a warning) instead of storing the Coupé sheet as the Cabriolet's; the XK Cabriolet 2005-2014 therefore has no card.
- **MG**: mgmotor.de publishes one current sheet per model/drivetrain without build years; `buildYearFrom` is only set where the filename carries one (`MY-23`, standard-convention filename), and `parseConfidence` is `heuristic`.
- **KBA model names**: KBA counts several of these brands' models under other names - Mazda "2"/"3"/"6" (Mazda2/3/6), Volvo "40"/"60"/"70"/"80"/"90" (S/V40 ... S/V90), MG "4"/"5"/"S5" (MG4/MG5/MGS5) and "MARVEL" (Marvel R). `model-aliases.json` maps them. MG HS/EHS are mapped to KBA's "MG ROEWE RX6" (the only unmatched MG series of plausible size) - unverified.
- **KGM / MG - English files behind German labels**: KGM's newer sheets (8 of 19) and MG's "MG4 Electric" sheet are the English edition, which neither the page nor the filename reveals (found by checking the text layer of every downloaded PDF). The sources list these files explicitly and record them as `languageCode: "EN"`; a sheet added later is assumed German until checked.
- **Mitsubishi**: the sheets come from Mitsubishi Austria's own site (`mitsubishi-motors.at/services/rettungskarten`, German, 16 sheets). `mitsubishi-motors.de` links no sheets itself - it only embeds the importer's PressMatrix catalogue `mitsubishi-publikationen.de`, whose robots.txt (`User-agent: * / Disallow: /`) forbids automated access - and Mitsubishi's global site only has sheets for Oceania. The Austrian page covers the current European range; older generations sold in Germany (Lancer, Pajero, i-MiEV, Colt before 2023, ...) are not on it. Each heading's "MY20" is read as the start year. The L200 is registered as a truck, so FZ12 (passenger cars) doesn't list it and its cards stay `bundlePriority: unknown`.
- **Subaru**: one combined PDF (~41MB, 57 pages) for all models; `split subaru` splits it by page text. Its bookmarks are left over from an older edition and miss half the pages, so they aren't used. Two older Legacy cards (saloon/estate, 2004–2009) have identical headers and end up as one two-page part.
- **Suzuki**: a few sheets (`.PDF` files with an upper-case extension, e.g. `Jimny-GJ.PDF`) are answered with 502 by Suzuki's CloudFront distribution and stay metadata only; when a label states no year, the category ("Modelle bis 2020") supplies the end year.
- **BYD**: taken from the Austrian importer's site (`bydauto.at`) - there is no German BYD page with rescue sheets. Several models (ATTO 2, SEAL 6, DOLPHIN G, TANG) aren't in KBA's FZ12 yet.
- **Polestar**: no source - no Polestar page (support, manual, first-responder) links the rescue-sheet PDFs, and their URLs embed upload timestamps, so they can't be discovered without a headless browser. The brand reports "not implemented" with that reason.
- **Isuzu, MAXUS, RUF, StreetScooter** aren't listed in KBA's FZ12 (below the publication threshold), so their cards stay `bundlePriority: unknown` (Rolls-Royce likewise); `kba-unlisted-models.json` lists these brands as a whole (`"modelName": "*"`).

### To verify

Assumptions made during implementation that haven't been confirmed against an authoritative source. Each affects only metadata (`modelName`/`languageCode`) or the KBA match (`estimatedFleetSize`/`bundlePriority`), never which PDFs are downloaded.

- **Mercedes-EQ in FZ12**: FZ12 2026 lists no EQ model series, although every EQ model is far above KBA's 1,000-vehicle threshold. The EQ models are therefore mapped to the related combustion series in `model-aliases.json` (EQA → GLA, EQB → GLB, EQC → "GLK, GLC", EQE → E-KLASSE, EQS → S-KLASSE, EQV → V-KLASSE; AMG EQE/EQS and Maybach EQS likewise), so their fleet size is that series' figure, not an EQ-only one. To confirm how KBA actually counts EQ models.
- **MG HS/EHS → "MG ROEWE RX6"**: RX6 is the only unmatched MG series of plausible size (11,787 vehicles); the mapping is inferred, not documented. Without it MG drops from 80 % to 60 % KBA-matched cards.
- **KBA's grouped series names (inferred)**: Volvo S40/V40 → "40", S60/V60 → "60", C70/V70 → "70", S80 → "80", S90/V90 → "90", EC40 → C40, EX40 → XC40; Mazda2/3/5/6 → "2"/"3"/"5"/"6"; Toyota Yaris Cross/GR Yaris → YARIS, Corolla Cross → COROLLA, Aygo X → AYGO; Subaru XV → CROSSTREK. FZ12 has no separate series for these models, so they are assumed to be counted inside the named one.
- **English sheets behind German labels (KGM, MG)**: the text layer of the PDFs shows that 8 of KGM's 19 sheets (Torres ×4, Actyon J120, Korando e-Motion, Musso EV, Musso Q300) and MG's `Rettungskarte-MG-4-MY-23.pdf` are English, although neither page nor filename says so. `KgmRescueCardSource`/`MgRescueCardSource` list these files explicitly as `EN`; a sheet the manufacturer adds later is assumed German until someone checks it.

### Licensing note

KBA vehicle stock data (FZ12) is published under "Datenlizenz Deutschland – Namensnennung – Version 2.0" (attribution required) — this is recorded in every `fz12_<year>.meta.json` and `fz12_<year>.json`. Manufacturer rescue data sheets remain the property of their respective manufacturers; several manufacturer pages state the sheets are intended for trained rescue personnel specifically.

---

## Deutsch

### Was das hier ist

Eine .NET 10 Konsolenanwendung für deutsche Feuerwehren, die:

1. **Rettungsdatenblätter** ("Rettungskarten" — PDFs mit Airbag-Positionen, Schneidzonen und Kraftstoff-/Batterie-Lage für ein bestimmtes Fahrzeugmodell) für **57 Automarken** herunterlädt — jede auf rettungskarten-service.de und beim ADAC gelistete Marke, für die der Hersteller selbst noch Rettungsdatenblätter veröffentlicht (Volkswagen-, Mercedes-Benz-, BMW- und Stellantis-Konzern, Renault, Toyota, Hyundai/Kia, Ford, Nissan, Volvo, Tesla, ... — siehe Tabelle unten). Als Quelle zählt nur, was der Hersteller selbst nennt (auch ein Portal/Dienstleister, auf das der Hersteller verweist, z. B. Stellantis Servicebox, IFZ Berlin, Scene7); Aggregatoren wie ADAC, rettungskarten-service.de oder Euro Rescue sind nie Quelle.
2. Die **KBA-Fahrzeugbestandsstatistik (FZ12)** herunterlädt — wie viele Fahrzeuge jeder Modellreihe tatsächlich in Deutschland zugelassen sind — und damit **priorisiert**, welche Rettungskarten verbreitet genug sind, um direkt in eine Einsatz-App gebündelt zu werden, statt nur auf Abruf verfügbar zu sein.
3. Alles in einer vorhersehbaren Ordnerstruktur auf der Festplatte ablegt, mit einer JSON-Metadaten-Datei je Rettungskarte (Baujahr, Modell, Hersteller, Schwestermodelle/Plattform-Geschwister, geschätzte Bestandsgröße, Bündel-Priorität).

Kein Hersteller veröffentlicht dafür eine stabile offizielle API — die Seitenstruktur jeder Marke musste einzeln reverse-engineered werden. Siehe die Marken-Statustabelle unten und die Code-Kommentare in `src/Rettungskarten.Infrastructure/RescueCards/` für Details zu den Rechercheergebnissen.

### Unterstützte Marken

| Marke | Status | Quelle |
|---|---|---|
| VW | Voll funktionsfähig | JSON-Feed auf `assets.feature-app.io` |
| Audi | Voll funktionsfähig | Statische HTML-Seite auf `audi.com` |
| Škoda | Voll funktionsfähig | Modellseiten auf `skoda-auto.de` |
| SEAT | Voll funktionsfähig | Modellseiten auf `seat.de` |
| Cupra | Voll funktionsfähig (nur Schweiz-Seite; Downloads der österreichischen Seite sind hinter einem Auth-Redirect) | Statische HTML-Seite auf `cupraofficial.ch` |
| Porsche | Voll funktionsfähig. Quelle sind 2 kombinierte PDFs (aktuelle + klassische Modelle); `fetch` lädt sie unverändert, `split porsche` ergänzt anschließend je eine Datei pro Modell, wie bei jeder anderen Marke | `porsche.com` offizielle Dokumente-Seite (Vue-SSR-Template eingebettet in einem `<script type="text/x-template">`-Block) |
| Bentley | Voll funktionsfähig | Statische HTML-Seite auf `bentleymotors.com` |
| Lamborghini | Voll funktionsfähig (nur englischsprachige Dokumente; Lamborghini bietet hierfür keine eigene deutsche Datei an) | Statische HTML-Seite auf `lamborghini.com` |
| Mercedes-Benz | Voll funktionsfähig | Statische HTML-Übersicht auf `rk.mb-qr.com` (das QR-Aufkleber-Portal) + Detailseite je Karte für den PDF-Link |
| Mercedes-AMG | Voll funktionsfähig | `rk.mb-qr.com` (dasselbe Portal und dieselbe Übersicht wie Mercedes-Benz) |
| Mercedes-EQ | Voll funktionsfähig | `rk.mb-qr.com` (dasselbe Portal und dieselbe Übersicht wie Mercedes-Benz) |
| Mercedes-Maybach | Voll funktionsfähig | `rk.mb-qr.com` (dasselbe Portal und dieselbe Übersicht wie Mercedes-Benz) |
| smart | Voll funktionsfähig | fortwo/forfour/roadster (von Mercedes gebaut, bis 2024) von `rk.mb-qr.com`; #1/#3/#5 (Joint Venture Mercedes/Geely, seit 2022) aus dem App-Bundle von `rescuecard.smart.com` |
| BMW | Voll funktionsfähig | JSON-API des BMW Group Aftersales Online System (`aos.bmwgroup.com`) |
| MINI | Voll funktionsfähig | JSON-API des BMW Group Aftersales Online System (`aos.bmwgroup.com`) |
| Rolls-Royce | Voll funktionsfähig | JSON-API des BMW Group Aftersales Online System (`aos.bmwgroup.com`) |
| Peugeot | Voll funktionsfähig | Statisches Frameset der Stellantis-Servicebox auf `public.servicebox-parts.com` (Modellseiten) |
| Citroën | Voll funktionsfähig | Statisches Frameset der Stellantis-Servicebox auf `public.servicebox-parts.com` (Modellseiten) |
| DS | Voll funktionsfähig | Statisches Frameset der Stellantis-Servicebox auf `public.servicebox-parts.com` (Modellseiten) |
| Opel | Voll funktionsfähig | JSON-API von IFZ Berlin (`ifz-berlin.de`, das von opel.de verlinkte Rettungsportal „Stellantis Aftersales") |
| Saab | Voll funktionsfähig | JSON-API von IFZ Berlin (`ifz-berlin.de`) |
| Chevrolet | Voll funktionsfähig | JSON-API von IFZ Berlin (`ifz-berlin.de`) |
| Cadillac | Voll funktionsfähig | JSON-API von IFZ Berlin (`ifz-berlin.de`) |
| Fiat | Nur Metadaten (robots.txt verbietet PDF-Downloads; `--ignore-robots-txt` lädt sie trotzdem, siehe [robots.txt ignorieren](#robotstxt-ignorieren---ignore-robots-txt); `split fiat` teilt dann das Sammel-PDF älterer Modelle auf) | Statische HTML-Seite auf `fiat.de` (`/besitzer/rettungsdatenblaetter`) |
| Fiat Professional | Nur Metadaten (robots.txt verbietet PDF-Downloads; `--ignore-robots-txt` lädt sie trotzdem, siehe [robots.txt ignorieren](#robotstxt-ignorieren---ignore-robots-txt)) | Statische HTML-Seite auf `fiat.de` (`/professional/besitzer/rettungsdatenblaetter`) |
| Jeep | Nur Metadaten (robots.txt verbietet PDF-Downloads; `--ignore-robots-txt` lädt sie trotzdem, siehe [robots.txt ignorieren](#robotstxt-ignorieren---ignore-robots-txt)) | Statische HTML-Seite auf `jeep.de` (`/mopar/rettungsdatenblaetter`) |
| Alfa Romeo | Nur Metadaten (robots.txt verbietet PDF-Downloads; `--ignore-robots-txt` lädt sie trotzdem, siehe [robots.txt ignorieren](#robotstxt-ignorieren---ignore-robots-txt)) | Statische HTML-Seite auf `alfaromeo.de` (`/rettungsdaten-blaetter`) |
| Lancia | Nur Metadaten (robots.txt verbietet PDF-Downloads; `--ignore-robots-txt` lädt sie trotzdem, siehe [robots.txt ignorieren](#robotstxt-ignorieren---ignore-robots-txt)) | Statische HTML-Seite auf `lancia.de` (`/mopar/rettungsdatenblaetter`) |
| Abarth | Nur Metadaten (robots.txt verbietet PDF-Downloads; `--ignore-robots-txt` lädt sie trotzdem, siehe [robots.txt ignorieren](#robotstxt-ignorieren---ignore-robots-txt); `split abarth` teilt dann das Sammel-PDF älterer Modelle auf) | Zwei statische HTML-Seiten auf `abarth.de` (Markenseite + Mopar-Seite) |
| Dodge | Voll funktionsfähig (nur Altmodelle bis 2013) | Statische HTML-Altseite auf `dodge.de` (nur http) |
| Maserati | Voll funktionsfähig (1 kombiniertes PDF von 2016, aufteilen mit `split maserati`) | Aftersales-Seite auf `maserati.com/de/de` |
| Renault | Voll funktionsfähig | Statische HTML-Seite auf `renault.de` (PDFs auf `cdn.group.renault.com`) |
| Dacia | Voll funktionsfähig | Statische HTML-Seite auf `dacia.de` (PDFs auf `cdn.group.renault.com`) |
| Toyota | Voll funktionsfähig | Statische HTML-Seite auf `toyota.de` (PDFs im Scene7-Speicher von Toyota Europe) |
| Lexus | Voll funktionsfähig | Statische HTML-Seite auf `lexus.de` (PDFs im Scene7-Speicher von Toyota Europe) |
| Daihatsu | Voll funktionsfähig (1 kombiniertes PDF, Aufteilung mit `split daihatsu`) | Kombiniertes PDF, verlinkt auf der Startseite von `daihatsu.de` |
| Hyundai | Voll funktionsfähig. Aktuelle Modelle pro Modell; Modelle vor 11/2019 nur als 1 kombiniertes PDF, `split hyundai` ergänzt je eine Datei pro Modell. Leitfäden "Maßnahmen im Notfall" (ERGs) werden nicht erfasst | Statische HTML-Seite auf `hyundai.com/de` (Download-Liste; PDFs auf Adobe Scene7 ohne `.pdf`-Endung) |
| Kia | Voll funktionsfähig. Einige Blätter gibt es nur auf Englisch (als `EN` übernommen); ältere Modelle (bis 08/2020) nur als 1 kombiniertes PDF, `split kia` ergänzt je eine Datei pro Modell | Statische HTML-Seite auf `kia.com/de` |
| Nissan | Voll funktionsfähig. Ältere Modelle (bis 06/2019) zusätzlich als 1 kombiniertes PDF, `split nissan` ergänzt je eine Datei pro Modell; ERGs ("Rettungsleitfaden") werden nicht erfasst | Statische HTML-Tabellen auf `nissan.de/rescuers_page.html` |
| Ford | Voll funktionsfähig. Modelle bis ca. 2020 nur im kombinierten PDF ("EU-rescue-cards-all-carlines", 204 Seiten), `split ford` ergänzt je eine Datei pro Modell; neuere Modelle pro Modell | Fahrzeugsuche im Ford-Serviceportal `fordserviceinfo.com` (verlinkt von ford.de), PDFs auf `fordservicecontent.com` |
| Mazda | Voll funktionsfähig (einige Datenblätter nur auf Englisch - Mazda bietet dafür keine deutsche Fassung an) | `mazda.de/rettungskarten` - Inhaltsblöcke als JSON in die Seite eingebettet (`window.mxp.data.push(JSON.parse('…'))`), Rettungskarten im FAQ-Block „ALLE RETTUNGSKARTEN“ |
| Honda | Voll funktionsfähig | Statische HTML-Seite auf `honda.de` (Modellüberschrift, Linktext und Absatz „Amtlicher Typ / Bauzeitraum“ je Datenblatt) |
| Volvo | Voll funktionsfähig | Statische HTML-Seite auf `volvocars.com` (Akamai - Browser-Client) |
| Jaguar | Voll funktionsfähig | Statische HTML-Seite auf `jaguar.com/de-de` |
| Land Rover | Voll funktionsfähig | Statische HTML-Seite auf `landrover.de` (Modell im `aria-label` der Links) |
| Tesla | Voll funktionsfähig (mehrere Datenblätter nur auf Englisch - Model Y, neuere Model S/X, Roadster) | Seite für Rettungskräfte `tesla.com/de_DE/firstresponders/vehicles-charging` (Akamai - Browser-Client), PDFs auf `digitalassets.tesla.com` |
| MG | Voll funktionsfähig (keine Baujahre veröffentlicht; das MG4-MY23-Datenblatt nur auf Englisch) | Statische HTML-Seite auf `mgmotor.de` |
| KGM (SsangYong) | Voll funktionsfähig (die neueren Datenblätter - Torres, Actyon, Korando e-Motion, Musso EV/Q300 - nur auf Englisch) | Statische HTML-Tabelle auf `kgm.de` |
| Mitsubishi | Voll funktionsfähig (aktuelle europäische Modellpalette: Space Star, Colt, ASX, Eclipse Cross, Outlander, L200; ältere in Deutschland verkaufte Generationen sind nicht abgedeckt) | Rettungskarten-Seite von Mitsubishi Österreich auf `mitsubishi-motors.at` (eine Karte je Datenblatt: Überschrift + PDF-Link) |
| Subaru | Voll funktionsfähig (1 kombiniertes PDF für die ganze Modellpalette, aufteilen mit `split subaru`) | Sicherheitsseite auf `subaru.de` (HubSpot-CMS) |
| Suzuki | Voll funktionsfähig | Dokumentseiten auf `auto.suzuki.de`: Übersicht → 4 Kategorieseiten (Modellkacheln nur im Next.js-RSC-Payload) → Modellseiten |
| BYD | Voll funktionsfähig (Seite des österreichischen Importeurs; BYD hat keine deutsche Seite mit Rettungskarten) | Download-Seite auf `bydauto.at` (Webflow-CMS) |
| Isuzu | Voll funktionsfähig | Statische HTML-Seite auf `isuzu-sales.de` |
| MAXUS | Voll funktionsfähig | Statische HTML-Seite auf `maxus.de` (Storyblok-Assets) |
| RUF | Voll funktionsfähig | Statische HTML-Seite auf `ruf-automobile.de` |
| StreetScooter | Voll funktionsfähig | Bestandskunden-Supportseite auf `streetscooter.com` |
| Polestar | Nicht implementiert: Polestar veröffentlicht seine Rettungsdatenblätter nur als einzelne PDFs unter `polestar.com/dato-assets/…`, die von keiner Seite verlinkt werden | – |

`list brands` zeigt jederzeit den aktuellen Status (inklusive Konzernzugehörigkeit jeder Marke).

**Nicht verfügbar** (keine offizielle Quelle mehr — diese Marken haben keinen `Brand`-Wert): Chrysler (Domain geparkt; Thema und Voyager sind über Lancia abgedeckt), Lada (Domain tot), Think City (Hersteller aufgelöst), Infiniti (Domain tot, 2020 aus Europa zurückgezogen), Datsun (nie in Deutschland verkauft), e.GO (insolvent, Seiten liefern 404), Aiways (Domain geparkt, nur noch ein Schweizer Importeur), Lynk & Co (keine Rettungsseite auf lynkco.com). Marken im KBA-Bestand, die auf keiner der beiden Übersichten stehen — Alpine, Ineos, Leapmotor, Xpeng, GWM/ORA, Aston Martin, Ferrari, Lotus, Iveco, MAN — sind Kandidaten für eine spätere Welle.

**Mögliche Erweiterung: Wohnmobile** (recherchiert, nicht umgesetzt — das KBA zählt Wohnmobile nur als „WOHNMOBILE ZUSAMMEN" bzw. nach Basisfahrzeug, z. B. FIAT DUCATO, eine Bestandszuordnung je Hersteller ist also nicht möglich): Dethleffs (`dethleffs.de/service/rettungskarten`, ~922 Blätter, Pfad `/…/<Modelljahr>/<Modell>-<a|i|t>-<Grundriss>.pdf`), Sunlight (`sunlight.de/infomaterial/#rettungskarten`, ein Dropbox-Ordner je Modelljahr, `dl=1` liefert ein ZIP, ~150–190 Blätter), CS-Reisemobile (`cs-reisemobile.de/…/rettungskarten.htm`, 31 Blätter, die Nummer im Namen ist die Sprinter-Baureihe 901–907).

### Schnellstart

Voraussetzung: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build
dotnet run --project src/Rettungskarten.Cli -- list brands
```

### CLI-Nutzung

```bash
# Rettungskarten für eine Marke (oder "all") herunterladen
dotnet run --project src/Rettungskarten.Cli -- fetch rescue-cards --brand skoda --output ./data/rescue-cards

# Nur Discovery + Parsing, keine PDF-Downloads
dotnet run --project src/Rettungskarten.Cli -- fetch rescue-cards --brand audi --dry-run

# Alle Marken außer einigen (durch Leerzeichen getrennt)
dotnet run --project src/Rettungskarten.Cli -- fetch rescue-cards --brand all --exclude-brands volvo tesla

# robots.txt nur für die genannten Marken ignorieren - vorher „robots.txt ignorieren" unten lesen
dotnet run --project src/Rettungskarten.Cli -- fetch rescue-cards --brand fiat --ignore-robots-txt fiat

# KBA-Fahrzeugbestand (FZ12) für ein Bezugsjahr laden
dotnet run --project src/Rettungskarten.Cli -- fetch stock --year 2026

# Rettungskarten mit Bestandsdaten abgleichen -> Bündel-Priorität
dotnet run --project src/Rettungskarten.Cli -- prioritize

# Kombinierte Alle-Modelle-PDF(s) einer Marke (zuvor geladen) in je eine Datei pro Modell aufteilen
dotnet run --project src/Rettungskarten.Cli -- split porsche
dotnet run --project src/Rettungskarten.Cli -- split all

# Implementierungsstatus der Marken auflisten
dotnet run --project src/Rettungskarten.Cli -- list brands

# Entwicklerwerkzeug: Schema einer XLSX-Datei prüfen
dotnet run --project src/Rettungskarten.Cli -- inspect xlsx pfad/zur/datei.xlsx

# Bereits geladene Rettungskarten-Metadaten auf Auffälligkeiten prüfen (siehe „Datenqualitäts-Prüfung" unten)
dotnet run --project src/Rettungskarten.Cli -- inspect quality --rescue-cards-path ./data/rescue-cards
```

Jeder Befehl unterstützt `--verbose` für ausführliche Logs.

### robots.txt ignorieren (`--ignore-robots-txt`)

> **Haftungshinweis:** Mit `--ignore-robots-txt` lädt das Werkzeug Dateien, die automatisierte Programme laut `robots.txt` der Seite nicht abrufen sollen. Die `robots.txt` ist der erklärte Wunsch des Seitenbetreibers, keine Zugriffssperre: sie zu ignorieren kann gegen die Nutzungsbedingungen der Seite verstoßen, und der Betreiber kann das Werkzeug sperren. **Ob diese Nutzung zulässig ist, verantworten allein Sie** — im Zweifel beim Hersteller um Erlaubnis bitten. Die Autoren übernehmen keine Haftung. Die geladenen Dateien nur für ihren Zweck verwenden: die Rettung von Menschen aus Fahrzeugen.

Standardmäßig beachtet jede Anfrage die `robots.txt` des Ziel-Hosts (siehe „Bekannte Einschränkungen“). `--ignore-robots-txt <marke...>` hebt das nur für die genannten Marken auf — ein „all“ gibt es nicht: jede andere Marke im selben Lauf beachtet die `robots.txt` weiterhin, auch auf einem gemeinsamen Host. Alles andere bleibt wie es ist: das Rate-Limit pro Host, Zeitlimits und die Behandlung von Weiterleitungen. Der Haftungshinweis oben wird bei jedem Lauf mit dieser Option ausgegeben; eine genannte Marke, die nicht Teil des Laufs ist (nicht per `--brand` gewählt oder ausgeschlossen), wird mit einer Warnung ignoriert.

Die Option gibt es für die Seiten von Fiat, Fiat Professional, Jeep, Alfa Romeo, Lancia und Abarth, deren `robots.txt` `*.pdf` verbietet — ohne sie gibt es für diese Marken nur Metadaten:

```bash
dotnet run --project src/Rettungskarten.Cli -- fetch rescue-cards --brand all --ignore-robots-txt fiat fiatprofessional jeep alfaromeo lancia abarth
```

### Sprache / Lokalisierung

Sämtliche Texte der Anwendung (Hilfe, Statustabellen, Fehlermeldungen) sind auf **Deutsch und Englisch** lokalisiert. Mit `--lang de` / `--lang en` explizit wählbar; ohne Angabe folgt die App der Betriebssystem-/Umgebungssprache und fällt für alles andere auf Englisch zurück.

```bash
dotnet run --project src/Rettungskarten.Cli -- --lang de list brands
```

Übersetzungen liegen in `src/Rettungskarten.Core/Localization/Strings.resx` (Englisch, neutrale Fallback-Ressource) und `Strings.de.resx` (Deutsch). Eine weitere Sprache wird durch eine zusätzliche `Strings.<kultur>.resx` mit denselben Schlüsseln ergänzt — dafür sind nur `Strings.ParseLanguageOption` und die `--lang`-Werteliste im CLI zu erweitern.

### Ordnerstruktur auf der Festplatte

```
data/
  rescue-cards/
    <marke>/<modell>/<id>.json   (+ .pdf, falls Download erfolgreich)
    <marke>/_manifest.json       (abgeleitet, aggregiert alle Sidecars dieser Marke; neu geschrieben von fetch, split und prioritize)
  stock/
    <jahr>/fz12_<jahr>.xlsx      (Rohdatei)
    <jahr>/fz12_<jahr>.json      (geparst)
    <jahr>/fz12_<jahr>.meta.json
  priority-report.json          (eine Zeile je Modell, sortiert nach geschätzter Bestandsgröße)
  priority-report.csv           (gleiche Daten, für Excel/Tabellenkalkulation)
```

`<marke>` ist der Markenname in Kleinbuchstaben wie bei `--brand` (z. B. `mercedesbenz`, `landrover`). Neben den geparsten Modelldaten hält jede Sidecar-Datei fest:

- `manufacturerGroup` — der Konzern (z. B. `volkswagenGroup`, `stellantis`, `independent`); auch eine Spalte in `priority-report.json`/`.csv`. smart ist die einzige Marke, deren Konzern von der Quelle abhängt (Mercedes-Benz bis 2021, Geely ab 2022).
- `documentScope` — `single` (ein Modell pro Datei), `combined` (eine Datei für viele Modelle, z. B. Porsches Alle-Modelle-PDF) oder `splitPart` (von `split` aus einer kombinierten Datei herausgeschnitten; `splitSourceId` verweist auf den kombinierten Eintrag, der erhalten bleibt). `combined`-Einträge sind keine Zeilen in `priority-report.json`/`.csv` — ihre Teile schon.
- `chassisCode` — der Baureihen-/Plattformcode des Herstellers, sofern die Quelle ihn nennt (z. B. `W177`, `F45`), sonst `null`.
- `fuelType` — über alle Quellen auf ein Vokabular vereinheitlicht: `Petrol`, `Diesel`, `Petrol/Diesel` (ein Datenblatt für beide), `Electric`, `Hybrid`, `Mild Hybrid`, `Plug-in Hybrid`, `Hydrogen`, `CNG`, `LPG`, `Ethanol` (englische Werte wie bei den Modellnamen; `Rettungskarten.Core/Models/FuelTypes.cs`). Eine Angabe der Quelle außerhalb davon (z. B. Mercedes' „Hybrid Benzin“) bleibt wie geschrieben; `null`, wenn die Quelle nichts angibt.

Sidecars, die vor diesen Feldern geschrieben wurden, werden mit den Standardwerten gelesen (`single`, Standardkonzern der Marke, kein Baureihencode).

### Für Entwickler

**Solution-Aufbau** (4 Projekte, `Rettungskarten.slnx`):

- `Rettungskarten.Core` — Domänenmodelle, Schnittstellen (`IRescueCardSource`, `IVehicleStockSource`, ...), Orchestrierung, Priorisierungslogik und die gemeinsamen Lokalisierungs-Strings. Keine I/O.
- `Rettungskarten.Infrastructure` — HTTP-Client-Setup (mit Rate-Limiting pro Host), AngleSharp-basiertes HTML-Parsing, ClosedXML-basiertes XLSX-Parsing, die Markenquellen (ein Ordner je Mehrmarken-Portal, z. B. `RescueCards/Mercedes/`, `RescueCards/Stellantis/`), den Splitter für kombinierte PDFs mit seinen Marken-Layouts, die KBA-Bestandsquelle und Dateisystem-Speicherung.
- `Rettungskarten.Cli` — Einstiegspunkt und Befehle auf Basis von `System.CommandLine`.
- `Rettungskarten.Tests` — xUnit-Tests, inklusive einer Regressionssuite, die den KBA-XLSX-Parser gegen eine echte heruntergeladene Datei prüft (`tests/Rettungskarten.Tests/Fixtures/fz12_2026.xlsx`). Jede Markenquelle (und die KBA-Bestandsquelle) hat einen Test auf `DiscoverAsync`-/`FetchWithRawAsync`-Ebene gegen eine Stub-`IHttpClientFactory` und eine echte, gekürzte HTML-/XLSX-Fixture — nicht nur die gemeinsamen Parser-Klassen für sich genommen —, da die Parser allein z. B. weder den VW-Sprach-Pfad-Bug noch den Audi-Feldverschiebungs-Bug aus dieser Session erkannt hätten.

**Neue Marke hinzufügen**: `Brand`-Enum-Wert ergänzen (allein dadurch ist sie gültiges `--brand`/`split`-Argument und Teil von `--brand all`), in `BrandGroups` zuordnen (Konzern und, falls das KBA ihre Fahrzeuge unter dem Namen einer anderen Marke zählt, Muttermarke) und, falls das KBA sie anders schreibt, in `BrandNames` (hat FZ12 gar keine Zeile für sie, stattdessen als `"*"` in `kba-unlisted-models.json` eintragen); ihre `IRescueCardSource` in `Rettungskarten.Infrastructure/RescueCards/` implementieren und in `Rettungskarten.Cli/CompositionRoot.cs` registrieren (eine Marke darf mehrere Quellen haben — jede wird durchsucht, jeder Eintrag wird über die Quelle geladen, die ihn gefunden hat); `Brand_<Marke>_Source` (und, falls die Abdeckung von der deutschsprachigen Einzelmodell-Norm abweicht, `Brand_<Marke>_Status`) in beiden Ressourcendateien für `list brands` ergänzen. Die meisten Quellen bauen auf gemeinsamen Bausteinen auf: `HtmlPdfLinkRescueCardSource` (statische Seite(n) mit direkten PDF-Links), `StandardRescueSheetFilenameParser` (die branchenweite Konvention `Marke_Modell_Aufbau_Jahr_Türen_Kraftstoff_SPRACHE.pdf`), `RescueSheetLabelParser` (Freitext-Labels), `RescueDocumentClassifier` (sortiert ERGs, Legenden und Handbücher aus) und `LanguagePreference` (Deutsch, Englisch nur als Ersatz). `DiscoverAsync` darf niemals wegen Parsing-Problemen bei einem einzelnen Modell werfen; `DownloadAsync` muss bei erwarteten HTTP-Fehlern ein fehlgeschlagenes `RescueCardDownloadResult` zurückgeben statt zu werfen — der Orchestrator behandelt nur eine Exception aus `DiscoverAsync` als Markenfehler. Quellen, deren echte PDF-URL kurzlebig ist oder erst über einen Zwischenschritt erreichbar ist, überschreiben `RescueCardSourceBase.ResolveDownloadUrlAsync`, statt sie schon bei der Discovery aufzulösen.

**Eine kombinierte Multi-Modell-PDF aufteilen**: `split <marke|all>` schneidet die kombinierten Alle-Modelle-PDFs einer Marke in je eine Datei pro Modell. Der markenunabhängige Teil (Lesen mit `PdfPig`, Kopieren der Seitenbereiche in eigenständige PDFs mit `PDFsharp`) ist `Rettungskarten.Infrastructure/RescueCards/Splitting/CombinedPdfSplitter.cs`; wie das Dokument einer Marke Seiten auf Modelle abbildet, beschreibt ein in `CombinedPdfLayouts.cs` registriertes `ICombinedPdfLayout`. `PageTextCombinedPdfLayout` ist die wiederverwendbare Basis für den üblichen Fall, dass das Modell am Text jeder Seite erkennbar ist (Porsches Layout gruppiert Seiten über die dokumenteigene "ID no."-Fußzeile); keine der bisher gefundenen kombinierten PDFs hat brauchbare Lesezeichen, daher gibt es keine lesezeichenbasierte Basis. Der kombinierte Eintrag bleibt erhalten (`documentScope: combined`); jedes Teil wird ein `splitPart`-Eintrag, und ein erneuter `split`-Lauf ersetzt die bisherigen Teile eines Dokuments (erst werden die neuen Teile gespeichert, dann die alten entfernt). `split` listet die Seiten auf, die es ohne eigenen Modell-Kopf dem Modell davor zugeordnet hat, und die Seiten, die in keinem Teil gelandet sind — beides sieht auch so aus wie ein Modell, dessen Kopfzeile nicht mehr erkannt wird, also nach einer neuen Ausgabe des Herstellers prüfen —, und endet mit einem Exit-Code ungleich 0, wenn sich ein Dokument nicht aufteilen ließ (PDF fehlt auf der Festplatte, nicht lesbar, keine Modelle erkannt); die übrigen Dokumente werden trotzdem aufgeteilt. Das ist ein eigener Befehl statt Teil von `fetch`, damit ein erneuter Lauf nicht die große Quelldatei erneut herunterlädt — dieselbe Überlegung, die `prioritize` bereits als eigener Nachbearbeitungsschritt befolgt.

**Datenqualitäts-Prüfung**: `inspect quality --rescue-cards-path <pfad>` (`Rettungskarten.Core/Quality/DataQualityChecker.cs`) prüft bereits geladene Metadaten auf Auffälligkeiten, die eine reine „hat Discovery überhaupt Ergebnisse geliefert"-Prüfung nicht erkennt — eine `bodyType`, die eigentlich eine Jahreszahl ist (genau die Form eines echten Bugs, der einmal bei Audi gefunden wurde: ein verschobenes Feld hat `BodyType`/`BuildYearFrom` bei mehreren Karten stillschweigend verfälscht), eine `fuelType` aus reinen Ziffern, eine doppelte `id`, oder ein Modell, das bei `bundlePriority: unknown` feststeckt und nicht in `Config/kba-unlisted-models.json` steht (genau die Form echter Bugs: alle Cupra-Karten ohne Zuordnung, weil das KBA Cupra unter SEAT zählt, oder ein aufgeteiltes Blatt, das nach seiner Dokument-ID benannt ist). Diese Datei listet die Modelle, die FZ12 nicht als eigene Baureihe führt — zu selten, zu alt oder zu neu, kein Pkw, oder ein bewusst nicht zugeordnetes Sondermodell — jeweils mit Begründung; wird ein gelistetes Modell doch zugeordnet, gibt es eine Warnung (Exit-Code bleibt 0), damit der Eintrag geprüft werden kann. Diese beiden Prüfungen ergeben erst etwas, nachdem `prioritize` tatsächlich gelaufen ist, greifen also nicht im reinen Dry-Run-Ablauf von `link-check.yml`, sondern nur bei lokaler Ausführung nach der vollständigen `fetch` → `prioritize`-Pipeline. Ein neu nicht zugeordnetes Modell braucht entweder einen Eintrag in `model-aliases.json` oder in `kba-unlisted-models.json` — beide werden von Hand gepflegt; eine unscharfe Regel gibt es bewusst nicht. Da Discovery `bodyType`/`fuelType` auch im `--dry-run`-Modus schon befüllt, laufen die übrigen Prüfungen direkt gegen die Dry-Run-Ausgabe, ohne zusätzliche Downloads — siehe den `link-check.yml`-Schritt unten.

**Continuous integration** (`.github/workflows/`):

- `ci.yml` — Build, Test und ein `list brands`-Smoke-Test unter den Locales `en-US` und `de-DE`, bei jedem Push und PR.
- `link-check.yml` — ein wöchentlich geplanter (montags), rein entdeckender Lauf gegen alle von GitHubs Runnern erreichbaren Live-Quellen (`fetch rescue-cards --brand all --dry-run --exclude-brands …` plus `fetch stock`; die ausgeschlossenen Marken sperren GitHubs Runner, siehe „Bekannte Einschränkungen“ — ein rein informativer Schritt probiert jede davon und warnt, sobald eine wieder erreichbar ist, damit sie aus `EXCLUDED_BRANDS` im Workflow entfernt werden kann), gefolgt von einem `inspect quality`-Durchlauf über die Dry-Run-Ausgabe, damit sowohl eine umgebaute/verschobene Herstellerseite als auch eine Parsing-Regression innerhalb einer Woche auffällt statt stillschweigend zu veralten; auch manuell über den Actions-Tab startbar. Was bei einer neuen Quelle zu tun ist, damit dieser Workflow sie mit abdeckt, steht in `CLAUDE.md`.

**Tests ausführen**:

```bash
dotnet test
```

Die Mitwirkungsregeln des Projekts (Code/Commits/PRs auf Englisch, Build+Test+Review vor jedem Commit, README-Pflege) stehen in `CLAUDE.md`.

### Bekannte Einschränkungen

- **Porsche**: anders als bei jeder anderen Marke hat die Quelle keine Datei pro Modell — Porsche veröffentlicht eine kombinierte PDF für alle aktuellen Modelle (~55MB) plus eine zweite für klassische Modelle, beide von Porsches eigenem CDN (`files.porsche.com`). `fetch rescue-cards --brand porsche` lädt diese zwei Dateien unverändert (benötigt längere HTTP-Timeouts als bei den anderen Marken, konfiguriert in `HttpServiceCollectionExtensions`); `split porsche` teilt sie anschließend anhand von Modellgrenzen im Seitentext (gruppiert über die dokumenteigene "ID no."-Fußzeile, da keine PDF-Gliederung existiert) in je eine Datei pro Modell auf und legt die Modell-Einträge neben den zwei kombinierten an (die als `documentScope: combined` erhalten bleiben). Die tatsächliche Sprache des Dokuments ist Englisch, nicht Deutsch (Porsche bietet hierfür keine eigene deutsche Datei an) - der Inhalt ist auf Englisch (`languageCode: "EN"` bei diesen Einträgen, anders als bei jeder anderen Marke). Modellnamen/-jahre werden heuristisch aus Fließtext geparst. Die wenigen Blätter ganz ohne Kopfzeile (E-Hybrid-Ergänzungen, der 911 Speedster und das 992 Cabriolet in der Ausgabe 2025) sind in `Config/porsche-headerless-sheets.json` von Hand über ihre Dokument-ID benannt (anpassen, wenn Porsche umnummeriert oder ein solches Blatt hinzukommt - ein Blatt mit Kopfzeile nimmt immer die Kopfzeile); `PorscheCombinedPdfLayout` korrigiert außerdem eine Seite, deren Fußzeile eine falsche ID trägt. `split` meldet jedes mehrseitige Blatt, das nicht genau die Seiten enthält, die seine Fußzeile nummeriert („Page x of y“) - eine fehlende oder fremde Seite, etwa durch eine falsch gedruckte ID, bleibt so nicht unbemerkt. Ein unbekanntes Blatt ohne Kopfzeile fällt weiterhin auf seine ID als "Modellname" zurück (`parseConfidence: "unparsed"`), statt verworfen zu werden. Die Blätter im neueren Euro-NCAP-Layout am Ende der Ausgabe 2025 werden über ihre „WP0_…“-ID gruppiert, oder über ihre Kopfzeile, wo die ID nur ein Platzhalter ist („GB-?“, Panamera G3). `bodyType` wird ebenfalls aus diesem Fließtext extrahiert (z. B. „Cabriolet", „SUV"); `doors`/`fuelType` nicht, da Porsches Header-Text nie eine Türzahl nennt und Antriebs-/Kraftstoffinformationen (z. B. „E-Hybrid") in den Modellnamen selbst einfließen, statt separat angegeben zu werden.
- **robots.txt wird respektiert**: jede Anfrage wird von `RobotsTxtDelegatingHandler` gegen die `robots.txt` des Ziel-Hosts geprüft (Regeln nach RFC 9309 für das Produkt-Token `RettungskartenTool`, sonst `*`). Ein gesperrter Download wird mit dem Fehlergrund „durch robots.txt des Hosts gesperrt“ übersprungen, die Karte bleibt als reiner Metadaten-Eintrag erhalten — das betrifft die Stellantis-Markenseiten mit `Disallow: *.pdf$` (z. B. fiat.de), sofern sie nicht in `--ignore-robots-txt` genannt sind (siehe Haftungshinweis unter „robots.txt ignorieren“). Eine nicht abrufbare robots.txt (4xx) bedeutet keine Einschränkungen; ein 5xx-/Zeitüberschreitungs-/Netzwerkfehler wird protokolliert und für die darauf wartenden Anfragen genauso behandelt, aber nicht zwischengespeichert — die nächste Anfrage an diesen Host ruft sie erneut ab, damit ein kurzer Ausfall weder Karten kostet noch die Regeln eines Hosts für den Rest des Laufs abschaltet. Weiterleitungen folgt das Werkzeug selbst statt des HTTP-Stacks, damit jeder Zwischenschritt (z. B. BMWs signierte S3-Links) wie eine eigene Anfrage gegen die robots.txt und das Rate-Limit seines Hosts geprüft wird.
- **Bot-Schutz (Akamai)**: mehrere Herstellerseiten (die Stellantis-Markenseiten, Volvo, Tesla, Bentley, Fords Content-CDN) beantworten Anfragen ohne Browser-Merkmale mit 403. Ihre Quellen nutzen einen eigenen HTTP-Client `rettungskarten-browser`, der die Header eines Browsers sendet (User-Agent, `Accept-Language`, `Sec-Fetch-*`) und Antworten dekomprimiert; robots.txt gilt auch für ihn. Das funktioniert nur **unter Windows aus einem normalen Netz**. Getestet am 30.09./06.10.2026 mit identischem Code: die Akamai-Seiten (Bentley, Fiat, Fiat Professional, Abarth, Alfa Romeo, Lancia, Jeep, Maserati, Volvo, Tesla) beantworten denselben Client unter Linux auch aus demselben Netz und mit derselben öffentlichen IP mit 403 (WSL2, Ubuntu 24.04) - Akamai erkennt den Linux-TLS-Stack, nicht nur die Header - und sperren außerdem GitHub-gehostete Runner auch unter Windows. Suzuki sperrt nur GitHubs Runner (sein normaler Client funktioniert unter Linux aus einem normalen Netz). Einen Browser-TLS-Handshake nachzubilden, um daran vorbeizukommen, wurde bewusst nicht versucht. Diese Marken also unter Windows abrufen; `link-check.yml` überspringt alle elf mit `--exclude-brands` - sie sind stattdessen per lokalem Dry-Run zu prüfen (`fetch rescue-cards --brand <marke> --dry-run`), z. B. vor einem Release, und eine Marke wird aus der Liste im Workflow entfernt, sobald ein Lauf sie wieder erreicht.
- **Cupra**: nur die Schweiz-Seite ist angebunden; die Downloads der österreichischen Seite leiten auf ein Identity-/Auth-Gateway um.
- **Bentley**: die Pro-Sprache-PDF-Links jedes Modells verwenden je nach Veröffentlichungszeitpunkt ein *anderes* URL-Namensschema (schlicht, ausgeschriebene Sprache, `_Web`-Suffix, datiertes Suffix, ...) — es gibt kein einheitliches URL-Muster, das zuverlässig „Deutsch" bedeutet. `BentleyRescueCardSource` filtert stattdessen auf das sichtbare Button-Label des Links („DEUTSCHE"), das über alle gefundenen Schemata hinweg konsistent ist.
- **Lamborghini**: wie bei Porsche sind die Dokumente nur auf Englisch — Lamborghini veröffentlicht hierfür keine eigene deutsche Datei.
- Dateinamen/Linktexte der Rettungskarten werden heuristisch geparst (keine Marke veröffentlicht strukturierte Metadaten) — siehe `ParseConfidence` je Eintrag. Bei VW/SEAT/Cupra übernimmt das der gemeinsame `StandardRescueSheetFilenameParser`; Audi hat einen eigenen `AudiFilenameParser`, da dessen CMS gelegentlich Dateinamen erzeugt, die der gemeinsame Parser nicht korrekt verarbeiten kann (siehe Doc-Kommentar dieser Klasse). Auch Škodas `bodyType`/`doors`/`fuelType` werden aus den Modellseiten-Titeln extrahiert, wo angegeben (z. B. „Fabia Combi", „Citigo 3-Türer", „Octavia CNG") — nur ca. 30 % der Titel geben das explizit an, der Rest bleibt korrekterweise `null` statt geraten zu werden.
- Die FZ12-Datei des KBA listet nur Modellreihen mit ≥1.000 zugelassenen Fahrzeugen (deren eigene Veröffentlichungsschwelle); seltenere Modelle fallen auf `bundlePriority: unknown` zurück — genau das richtige "nur auf Abruf"-Signal für den Zweck dieses Werkzeugs. FZ12 zählt außerdem nur Pkw: als Lkw zugelassene Pick-ups und Transporter (Hilux, Amarok, Navara, NV400, ...) und Leichtkraftfahrzeuge (Ami, Rocks-e, Twizy) fehlen ganz. Beides steht in `Config/kba-unlisted-models.json` (siehe „Datenqualitäts-Prüfung“). Der Prioritätsbericht gruppiert Modellnamen so, wie sie abgeglichen werden (ohne Groß-/Kleinschreibung und Satzzeichen), „NAVARA“ und „Navara“ sind also eine Zeile.
- FZ12 hat keine feinere Aufschlüsselung als „Modellreihe" — keine Aufteilung nach Generation, Karosserieform oder Kraftstoffart (an einem echten Download verifiziert: nur die Spalten `Segment`/`Modellreihe`/`Anzahl`, keine weiteren). Jede Rettungskarten-Variante eines Modells (z. B. jeder Golf-Modelljahrgang/jede Karosserieform) bekommt daher dieselbe `estimatedFleetSize`/`bundlePriority` — eine feinere KBA-Quelle dafür gibt es nicht. `priority-report.json`/`.csv` trägt dem Rechnung, indem eine Zeile je (Marke, Modell) mit einer `cardCount` (Anzahl der Rettungskarten-Varianten) und einer `notDownloadedCount` (davon ohne lokale PDF) aufgeführt wird — so wirkt ein Modell mit „Hoch"-Priorität nicht fälschlich vollständig abgedeckt, wenn einzelne Downloads fehlgeschlagen sind —, statt dasselbe Modell mit denselben Zahlen einmal je Karte zu wiederholen.
- Die FZ12-Datei des KBA führt Cupra überhaupt nicht als eigene Marke — jedes Cupra-Modell wird stattdessen unter „SEAT" gezählt (`BrandGroups.ParentBrandOf` macht SEAT zur Muttermarke von Cupra, sodass `BrandNames` Cupra-Karten gegen SEAT-beschriftete Bestandszeilen abgleicht; derselbe Mechanismus deckt Mercedes-AMG/-EQ/Maybach → MERCEDES, und Fiat Professional/Abarth → FIAT ab). Das bedeutet: `estimatedFleetSize` einer Cupra-Karte ist die kombinierte SEAT+Cupra-Zulassungszahl für dieses Modell, keine reine Cupra-Zahl — die bestmögliche Näherung angesichts der KBA-Granularität, keine exakte Zählung.
- Porsches Rettungskarten verwenden feingranulare Generations-/Varianten-Namen (z. B. „911 Carrera", „Cayenne E-Hybrid"), die ohne Weiteres nicht zu den deutlich gröberen 9 vom KBA erfassten Porsche-Baureihen passen (911, Boxster, Cayman, Cayenne, Macan, Panamera, Taycan, 928, 968) — `model-aliases.json` ordnet die bekannten Varianten ihrer jeweiligen KBA-Basisbaureihe zu. Echte, vom KBA nicht erfasste Klassiker (356, 924, 944, 959, ...) bleiben zurecht bei `bundlePriority: unknown`. Bewusst *nicht* zugeordnet: extrem seltene Spezialmodelle (911 GT2/GT2 RS/GT3/GT3 RS/R/Turbo/Speedster, 911 G-Model Turbo, 718 Cayman GT4, Boxster Spyder; in `kba-unlisted-models.json` gelistet) — sie der aggregierten Bestandszahl der Basisbaureihe zuzuordnen würde ihre tatsächliche Verbreitung stärker verzerren als bei einer gewöhnlichen Ausstattungsvariante, daher ist `unknown` (das ehrliche „nur auf Abruf"-Signal) hier korrekter als eine fälschlich zu hohe Priorität.
- Alle JSON-Dateien (Rettungskarten-Sidecars, die KBA-Bestandsergebnis-/Meta-Dateien) werden atomar geschrieben (Temp-Datei + Umbenennen), sodass ein Prozessabbruch mitten im Schreiben keine abgeschnittene Datei hinterlassen kann. Ist eine Sidecar- oder Bestandsdatei trotzdem nicht lesbar (z. B. externe Einwirkung oder vor diesem Fix geschrieben), wird sie mit einer Warnung auf stderr übersprungen, statt den gesamten Befehl abzubrechen — bei einer Rettungskarten-Sidecar fehlt dadurch nur diese eine Karte im Ergebnis des Laufs, bis sie erneut geladen oder entfernt wird; bei der einzelnen KBA-Bestandsergebnisdatei fällt der gesamte `prioritize`-Lauf auf die übliche „kein Bestand gefunden"-Meldung zurück, da es bei nur einer Datei nichts teilweise zu retten gibt.
- **Mercedes-Benz / AMG / EQ / Maybach / smart (rk.mb-qr.com)**: ein Portal bedient alle fünf Marken; die ~1,4MB große Übersicht wird pro Lauf einmal geladen und geteilt. Der PDF-Link steht nur auf der Detailseite jeder Karte, daher stellt `fetch` ohne `--dry-run` zwei Anfragen je Karte (Detailseite + PDF) - die Discovery selbst bleibt bei einer Anfrage. Türanzahlen nennt die Übersicht nicht, sie bleiben `null`. Modellnamen sind die Klassennamen des Portals („A-Klasse", „GLC", „EQA"); die Motorbezeichnung landet in `variant`, die Typnummer (z. B. „W177") in `chassisCode`.
- **Mercedes-EQ / KBA**: FZ12 führt überhaupt keine EQ-Modellreihe, obwohl jedes EQ-Modell weit über der 1.000-Fahrzeuge-Schwelle liegt - das KBA zählt sie offenbar unter der verwandten Verbrenner-Baureihe. `model-aliases.json` ordnet EQA -> GLA, EQB -> GLB, EQC -> GLK/GLC, EQE -> E-Klasse, EQS -> S-Klasse, EQV -> V-Klasse zu; die resultierende Bestandszahl ist die der ganzen Baureihe, also eine Näherung. Dasselbe gilt für T-Klasse/EQT/eCitan (-> Citan), eVito (-> Vito), eSprinter (-> Sprinter) und Marco Polo (-> V-Klasse).
- **Maybach / AMG-Sondermodelle**: der Maybach 57/62 (2002-2013), Mercedes-AMG ONE, AMG PureSpeed und SLR McLaren sind bewusst nicht zugeordnet (das KBA führt sie nicht; eine Zuordnung zur Basisbaureihe würde ihre Priorität verfälschen) und bleiben bei `bundlePriority: unknown`.
- **smart #1/#3/#5**: rescuecard.smart.com ist eine Angular-App ohne API; die Modellliste wird aus dem kompilierten JavaScript-Bundle gelesen. Ändert smart beim Neubau der App die Form dieses Katalogs, schlägt die Discovery sichtbar fehl (Link-Check), statt stillschweigend nichts zu liefern. Ihre `manufacturerGroup` ist `geely`; die älteren smart-Generationen behalten `mercedesBenzGroup`.
- **BMW / MINI / Rolls-Royce**: alle drei kommen aus einer öffentlichen JSON-API (`aos.bmwgroup.com/api/v2/rescue-sheets`, nur die deutsche Liste - dort fehlt keinem Modell ein deutsches Blatt). Der PDF-Link, den sie liefert, ist eine vorsignierte S3-URL mit 2 Stunden Gültigkeit; gespeichert wird deshalb als `downloadUrl` der stabile Download-Endpunkt des Portals, der bei jedem Download auf eine frische signierte URL weiterleitet. Die Karosserieform stammt aus der portaleigenen Kategorie, die zu grob und gelegentlich falsch ist (MINIs neuer 5-Türer Cooper F65 unter „Clubman", der Cullinan unter „sedan", MINIs Kategorie „coupe" meint den 3-Türer); eine kleine Fahrgestellcode-Tabelle in `BmwGroupRescueSheetParser` korrigiert die bekannten Fälle. Elektrische i-Modelle werden als solche benannt (`i4`, `i5`, `i7`, `iX3`) und über `model-aliases.json` der KBA-Baureihe zugeordnet, unter der sie zugelassen sind (4ER, 5ER, 7ER, X3). Das KBA führt alle MINI-Modelle als eine einzige Baureihe „MINI", daher teilen sich alle MINI-Karten eine Bestandszahl; Rolls-Royce ist in FZ12 gar nicht gelistet (`bundlePriority: unknown`, erwartet).
- **Stellantis-Servicebox (Peugeot, Citroën, DS)**: die Modellseiten mischen zwei Tabellenlayouts und mindestens fünf PDF-Namensschemata und führen Emergency Response Guides auf denselben Seiten - der ERG-Abschnitt wird an seiner Überschrift („Handbuch zur Rettung (ERG)") erkannt, weil manche ERG-Dateien nicht so heißen. Die Metadaten stammen aus den Link-/Überschriftstexten, nicht aus den Dateinamen. Ältere Zeilen verwenden unbenannte Kraftstoff-Piktogramme; deren Kraftstoffart bleibt leer, wenn der Text sie nicht nennt. Der dokumentierte Host `public.servicebox.com` löst nicht mehr auf; `public.servicebox-parts.com` liefert dieselben Seiten.
- **IFZ Berlin (Opel, Saab, Chevrolet, Cadillac)**: die API antwortet nur mit der Portalseite als `Referer` und kennt keine Abfrage „alle Modelle", daher stellt die Suche eine Anfrage pro Modellgruppe (~75 für Opel, ca. 1,5 Minuten beim Standard-Ratenlimit). Vauxhall ist bewusst keine eigene Marke: die Opel-Sammlung von IFZ heißt „Opel/Vauxhall“ und enthält keine Vauxhall-eigenen Modelle - ihre englische Fassung sind dieselben Fahrzeuge, für den britischen Markt übersetzt, und das KBA zählt sie als Opel.
- **Fiat, Fiat Professional, Jeep, Alfa Romeo, Lancia, Abarth**: die deutschen Markenseiten verbieten `*.pdf` in ihrer robots.txt, daher erfassen diese Quellen nur Metadaten (Modell, Kraftstoffart, Türen, Quell-/Download-URL) - es wird kein PDF heruntergeladen, außer die Marke ist in `--ignore-robots-txt` genannt (vorher den Haftungshinweis lesen). Die meisten Link-Beschriftungen nennen kein Baujahr („Jeep® Compass e-Hybrid", „Fiat Tipo Kombi"), die Jahresfelder bleiben dort leer; die Datumsangaben in Stellantis-Dokumentcodes (`..._DE_01_06_23_T`) sind Ausgabedaten, keine Baujahre, und werden bewusst ignoriert.
- **Fiat-/Abarth-Sammlungen „ShedaSoccorso"**: fiat.de (auch von der Fiat-Professional-Seite verlinkt) und die Abarth-Mopar-Seite verlinken je ein markenweites Sammel-PDF älterer Modelle (Fiat: 43 Blätter, Abarth: 4, von 2010-2012). Es wird jeweils als `Combined`-Eintrag („Various Models") geführt und nur mit `--ignore-robots-txt` heruntergeladen (robots.txt verbietet es). `split fiat`/`split abarth` ergänzen dann je eine Karte pro Blatt: die Blätter zeigen ihr Modell nur als Bild, daher stammen die Modelle aus dem Inhaltsverzeichnis des Dokuments („FIAT GRANDE PUNTO LPG 3" = 3 Türen, LPG), über die Blattnummer in der Fußzeile dem jeweiligen Blatt zugeordnet; Baujahre stammen aus der Angabe „(AB_01/2010)"/„(BIS_12/2009)" auf dem Blatt selbst, da das Inhaltsverzeichnis das „ab"/„bis" weglässt. Die Kopie der Fiat-Sammlung auf der Fiat-Professional-Seite wird nicht aufgeteilt - ihre Blätter sind Fiat-Pkw.
- **Lancia**: die Seite beschriftet zwei verschiedene Ypsilon-LPG-Blätter identisch („Lancia Ypsilon 2011 LPG"), daher werden Lancia-Blätter aus ihren (konsistenten) Dateinamen statt aus den Beschriftungen gelesen.
- **Dodge**: nur die Altseite `dodge.de` (Dodge hat den deutschen Markt 2011 verlassen) führt noch deutsche Blätter - sechs Blätter für Caliber, Journey und Nitro, über reines http (der Host hat kein https).
- **Maserati**: Maserati Deutschland veröffentlicht nur ein kombiniertes PDF (05/2016, sechs Modelle auf je einer Seite). `split maserati` macht daraus Karten für GranCabrio, GranTurismo, Ghibli, Levante und zwei Quattroporte-Generationen; neuere Modelle (Grecale, MC20, GranTurismo ab 2023) sind darin nicht enthalten.
- **Renault/Dacia:** 57 der 74 Renault-Rettungskarten (Clio 1-4, Laguna, Scénic 1-3, die meisten E-Tech-Karten, ...) sind unter Dateinamen ohne Fahrzeugdaten veröffentlicht oder solchen, deren einzige Jahreszahl das Erstellungsdatum der Karte ist (2014), und ihre Beschriftung nennt nur Generation oder Ausstattung ("CLIO 3", "ZOE E-TECH 2") - diese Karten haben keine Baujahre. Bei Dacia stehen die Jahre immer in der Beschriftung.
- **Toyota/Lexus:** Für Benzin-/Dieselmodelle nennt die Seite keinen Antrieb, daher haben nur Hybrid-, Plug-in-, Elektro- und Wasserstoff-Karten eine Kraftstoffart. Lexus führt RX 350h/450h+ doppelt (Karten für Links- und Rechtslenker); beide werden übernommen.
- **Daihatsu:** Daihatsu hat Europa 2013 verlassen; das einzige kombinierte PDF (23 Karten bis 2011) besteht aus eingescannten Bildern ohne Text oder Lesezeichen, daher liest `split daihatsu` die Modellgrenzen aus der Übersichtstabelle auf Seite 2 des Dokuments.
- **Ford-Discovery ist bewusst langsam**: Ford veröffentlicht die Karten nur hinter einer Suche nach Modelljahr + Baureihe auf `fordserviceinfo.com`, und Karten hängen am einzelnen Modelljahr. Die Discovery fragt deshalb jede europäische Baureihe für jedes Modelljahr ab 2019 ab (~210 Anfragen, ~4-5 Minuten beim Höflichkeitslimit von 1 Anfrage/Sekunde). Abfragen für ältere Modelljahre liefern nur das kombinierte PDF und werden übersprungen. Der deutsche Markt wird über das `UserCountry`-Cookie gewählt, das auch das Länderformular des Portals setzt; ändert Ford das, schlägt die Discovery mit einer klaren Fehlermeldung fehl (und der wöchentliche Link-Check wird rot).
- **Kombinierte PDFs von Hyundai/Kia/Nissan/Ford** haben keine Lesezeichen; `split` erkennt Modellgrenzen am aufgedruckten Kopf jedes Blatts. Blätter, deren Kopf keine Baujahre nennt (einige Ford-Karten, z. B. "Transit Courier", "Explorer PHEV"), werden korrekt getrennt, aber als nicht geparst markiert.
- **Kia**: einige ältere Dateien tragen kein Jahr im Namen ("Kia-EV6-Rettungsdienste.pdf", "Kia-Sportage-PHEV.pdf"); diese Karten haben kein Baujahr.
- **Mazda / Tesla**: für einige Modelle gibt es nur ein englisches Rettungsdatenblatt (Mazda: Mazda3 und CX-30 ab 2023, CX-60 e-Skyactiv D; Tesla: Model S/X 2021 und 2022+, alle Model-Y-Datenblätter, Roadster). Diese werden mit `languageCode: "EN"` übernommen; wo es für dieselbe Generation ein deutsches Datenblatt gibt, wird nur das deutsche behalten. Die Sprache wird aus dem Dateinamen bestimmt (Mazdas Dokumentcodes `rsde`/`rsen`, Teslas Suffix `_de`/`_en`), nicht aus den deutschen Linktexten, die Tesla auch für englische Dateien verwendet.
- **Mazda**: die Seite nennt zu jedem Datenblatt FIN-Bereiche (z. B. `JMZKE******100000`); sie sind nicht Teil des Metadaten-Schemas und werden verworfen. Mazdas Generationscode (KE, KF, BP, ...) wird als `chassisCode` gespeichert.
- **Jaguar**: die Live-Seite verlinkt „XK Cabriolet (2005-2014)“ auf die PDF des XK *Coupé*. `JaguarRescueCardSource` überspringt einen Link, dessen Beschriftung und Dateiname verschiedene Karosserieformen nennen (mit Warnung), statt das Coupé-Datenblatt als das des Cabriolets zu speichern; für das XK Cabriolet 2005-2014 gibt es daher keine Karte.
- **MG**: mgmotor.de veröffentlicht je Modell/Antrieb ein aktuelles Datenblatt ohne Baujahre; `buildYearFrom` ist nur gesetzt, wo der Dateiname eines enthält (`MY-23`, Dateiname nach Standard-Konvention), `parseConfidence` ist `heuristic`.
- **KBA-Modellnamen**: das KBA führt mehrere Modelle dieser Marken unter anderen Namen - Mazda „2“/„3“/„6“ (Mazda2/3/6), Volvo „40“/„60“/„70“/„80“/„90“ (S/V40 ... S/V90), MG „4“/„5“/„S5“ (MG4/MG5/MGS5) und „MARVEL“ (Marvel R). `model-aliases.json` bildet sie ab. MG HS/EHS sind auf KBAs „MG ROEWE RX6“ abgebildet (die einzige nicht zugeordnete MG-Baureihe plausibler Größe) - nicht verifiziert.
- **KGM / MG - englische Dateien hinter deutschen Beschriftungen**: KGMs neuere Datenblätter (8 von 19) und MGs „MG4 Electric“-Datenblatt sind die englische Fassung, was weder Seite noch Dateiname verrät (gefunden durch Prüfen der Textebene jeder heruntergeladenen PDF). Die Quellen führen diese Dateien explizit auf und speichern sie mit `languageCode: "EN"`; ein später hinzukommendes Datenblatt gilt bis zur Prüfung als deutsch.
- **Mitsubishi**: die Datenblätter stammen von der eigenen Seite von Mitsubishi Österreich (`mitsubishi-motors.at/services/rettungskarten`, deutsch, 16 Datenblätter). `mitsubishi-motors.de` verlinkt selbst keine Datenblätter, sondern bettet nur den PressMatrix-Katalog des Importeurs `mitsubishi-publikationen.de` ein, dessen robots.txt (`User-agent: * / Disallow: /`) automatisierten Zugriff verbietet - und die globale Mitsubishi-Seite hat Datenblätter nur für Ozeanien. Die österreichische Seite deckt die aktuelle europäische Modellpalette ab; ältere in Deutschland verkaufte Generationen (Lancer, Pajero, i-MiEV, Colt vor 2023, ...) fehlen dort. Das „MY20" jeder Überschrift wird als Startjahr gelesen. Der L200 ist als Lkw zugelassen, daher führt FZ12 (Pkw) ihn nicht und seine Karten bleiben bei `bundlePriority: unknown`.
- **Subaru**: ein kombiniertes PDF (~41MB, 57 Seiten) für alle Modelle; `split subaru` teilt es anhand des Seitentexts auf. Seine Lesezeichen stammen noch aus einer älteren Ausgabe und decken die Hälfte der Seiten nicht ab, werden daher nicht verwendet. Zwei ältere Legacy-Karten (Limousine/Kombi, 2004–2009) haben identische Kopfzeilen und landen in einem gemeinsamen zweiseitigen Teil.
- **Suzuki**: einige Blätter (`.PDF`-Dateien mit großgeschriebener Endung, z. B. `Jimny-GJ.PDF`) beantwortet Suzukis CloudFront-Distribution mit 502, sie bleiben reine Metadaten-Einträge; nennt ein Linktext kein Jahr, liefert die Kategorie („Modelle bis 2020") das Endjahr.
- **BYD**: stammt von der Seite des österreichischen Importeurs (`bydauto.at`) — eine deutsche BYD-Seite mit Rettungskarten gibt es nicht. Mehrere Modelle (ATTO 2, SEAL 6, DOLPHIN G, TANG) sind noch nicht in der FZ12 des KBA.
- **Polestar**: keine Quelle — keine Polestar-Seite (Support, Handbuch, Einsatzkräfte) verlinkt die Rettungsdatenblatt-PDFs, und deren URLs enthalten Upload-Zeitstempel, sodass sie ohne Headless-Browser nicht auffindbar sind. Die Marke meldet „nicht implementiert" mit diesem Grund.
- **Isuzu, MAXUS, RUF, StreetScooter** stehen nicht in der FZ12 des KBA (unter der Veröffentlichungsschwelle), ihre Karten bleiben daher bei `bundlePriority: unknown` (ebenso Rolls-Royce); `kba-unlisted-models.json` listet diese Marken als Ganzes (`"modelName": "*"`).

### Zu prüfen

Annahmen aus der Umsetzung, die noch nicht gegen eine verlässliche Quelle bestätigt sind. Sie betreffen nur Metadaten (`modelName`/`languageCode`) oder den KBA-Abgleich (`estimatedFleetSize`/`bundlePriority`), nie welche PDFs geladen werden.

- **Mercedes-EQ in FZ12**: FZ12 2026 führt keine EQ-Modellreihe, obwohl jedes EQ-Modell weit über der 1.000-Fahrzeuge-Schwelle des KBA liegt. Die EQ-Modelle sind deshalb in `model-aliases.json` der verwandten Verbrenner-Baureihe zugeordnet (EQA → GLA, EQB → GLB, EQC → „GLK, GLC“, EQE → E-KLASSE, EQS → S-KLASSE, EQV → V-KLASSE; AMG EQE/EQS und Maybach EQS ebenso), ihre Bestandszahl ist also die dieser Baureihe, keine reine EQ-Zahl. Zu klären, wie das KBA EQ-Modelle tatsächlich zählt.
- **MG HS/EHS → „MG ROEWE RX6“**: RX6 ist die einzige nicht zugeordnete MG-Baureihe plausibler Größe (11.787 Fahrzeuge); die Zuordnung ist abgeleitet, nicht belegt. Ohne sie sinkt der Anteil der MG-Karten mit KBA-Treffer von 80 % auf 60 %.
- **Zusammengefasste KBA-Baureihen (abgeleitet)**: Volvo S40/V40 → „40“, S60/V60 → „60“, C70/V70 → „70“, S80 → „80“, S90/V90 → „90“, EC40 → C40, EX40 → XC40; Mazda2/3/5/6 → „2“/„3“/„5“/„6“; Toyota Yaris Cross/GR Yaris → YARIS, Corolla Cross → COROLLA, Aygo X → AYGO; Subaru XV → CROSSTREK. FZ12 hat für diese Modelle keine eigene Baureihe, sie werden daher als in der genannten mitgezählt angenommen.
- **Englische Datenblätter hinter deutschen Labels (KGM, MG)**: die Textebene der PDFs zeigt, dass 8 der 19 KGM-Datenblätter (Torres ×4, Actyon J120, Korando e-Motion, Musso EV, Musso Q300) und MGs `Rettungskarte-MG-4-MY-23.pdf` englisch sind, obwohl weder Seite noch Dateiname das angeben. `KgmRescueCardSource`/`MgRescueCardSource` führen diese Dateien ausdrücklich als `EN`; ein später vom Hersteller ergänztes Blatt gilt als deutsch, bis es jemand prüft.

### Hinweis zur Lizenzierung

KBA-Fahrzeugbestandsdaten (FZ12) werden unter "Datenlizenz Deutschland – Namensnennung – Version 2.0" veröffentlicht (Namensnennung erforderlich) — das wird in jeder `fz12_<jahr>.meta.json` und `fz12_<jahr>.json` festgehalten. Die Rettungsdatenblätter der Hersteller bleiben Eigentum der jeweiligen Hersteller; mehrere Herstellerseiten weisen ausdrücklich darauf hin, dass die Blätter für geschultes Rettungspersonal bestimmt sind.
