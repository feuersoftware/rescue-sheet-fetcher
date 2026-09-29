# Rettungskarten & Vehicle Stock Tool

**Languages:** [🇬🇧 English](#english) | [🇩🇪 Deutsch](#deutsch)

---

## English

### What this is

A .NET 10 console application for German fire brigades that:

1. Downloads **rescue data sheets** ("Rettungskarten"/"Rettungsdatenblätter" — PDFs showing airbag locations, cut zones, and fuel/battery placement for a specific vehicle model) for **Volkswagen Group brands**: VW, Audi, Škoda, SEAT, Cupra, Porsche, Bentley, Lamborghini.
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

Run `list brands` any time for the current status.

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
    <brand>/_manifest.json      (derived, aggregates that brand's sidecars)
  stock/
    <year>/fz12_<year>.xlsx     (raw download)
    <year>/fz12_<year>.json     (parsed)
    <year>/fz12_<year>.meta.json
  priority-report.json          (one row per model, sorted by estimated fleet size)
  priority-report.csv           (same data, for Excel/spreadsheet use)
```

`<brand>` is the lower-cased brand name as used by `--brand` (e.g. `mercedesbenz`, `landrover`). Besides the parsed model data, every sidecar records:

- `manufacturerGroup` — the corporate group (e.g. `volkswagenGroup`, `stellantis`, `independent`); also a column of `priority-report.json`/`.csv`. smart is the one brand whose group depends on the source (Mercedes-Benz up to 2021, Geely from 2022).
- `documentScope` — `single` (one model per file), `combined` (one file covering many models, e.g. Porsche's all-models PDF) or `splitPart` (cut out of a combined file by `split`, with `splitSourceId` pointing at the combined entry, which is kept).
- `chassisCode` — the manufacturer's series/platform code where the source states it (e.g. `W177`, `F45`), otherwise `null`.

Sidecars written before these fields existed are read with the defaults (`single`, the brand's default group, no chassis code).

### For developers

**Solution layout** (4 projects, `Rettungskarten.slnx`):

- `Rettungskarten.Core` — domain models, interfaces (`IRescueCardSource`, `IVehicleStockSource`, ...), orchestration, priority-matching logic, and the shared localization strings. No I/O.
- `Rettungskarten.Infrastructure` — HTTP client setup (with per-host rate limiting), AngleSharp-based HTML parsing, ClosedXML-based XLSX parsing, the eight brand sources, the KBA stock source, and file-system storage.
- `Rettungskarten.Cli` — `System.CommandLine`-based entry point and command implementations.
- `Rettungskarten.Tests` — xUnit tests, including a regression suite that runs the KBA XLSX parser against a real downloaded file (`tests/Rettungskarten.Tests/Fixtures/fz12_2026.xlsx`). Every brand source (and the KBA stock source) has a `DiscoverAsync`/`FetchWithRawAsync`-level test against a stub `IHttpClientFactory` and a real, trimmed HTML/XLSX fixture — not just the shared parser classes' own unit tests — since the shared parsers alone wouldn't have caught e.g. the VW language-bucket-path bug or the Audi filename-parser field-shift bug found this session.

**Adding a new brand**: add its `Brand` enum value (that alone makes it a valid `--brand`/`split` argument and part of `--brand all`), map it in `BrandGroups` (group, and parent brand if KBA counts its vehicles under another brand's name) and, if KBA spells it differently, in `BrandNames`; implement its `IRescueCardSource` in `Rettungskarten.Infrastructure/RescueCards/` and register it in `Rettungskarten.Cli/CompositionRoot.cs` (a brand may have several sources — every one is discovered, each entry downloads through the source that found it); add `Brand_<Brand>_Source` (and, if coverage differs from the per-model German norm, `Brand_<Brand>_Status`) to both resource files for `list brands`. Most sources derive from shared building blocks: `HtmlPdfLinkRescueCardSource` (static page(s) with direct PDF links), `StandardRescueSheetFilenameParser` (the industry-wide `Brand_Model_Body_Year_Doors_Fuel_LANG.pdf` convention), `RescueSheetLabelParser` (free-text labels), `RescueDocumentClassifier` (drops ERGs, legends, manuals) and `LanguagePreference` (German, English only as a fallback). `DiscoverAsync` must never throw for a single model's parsing trouble; `DownloadAsync` must return a failed `RescueCardDownloadResult` rather than throwing for expected HTTP failures — the orchestrator only treats an exception from `DiscoverAsync` as a brand-level failure. Sources whose real PDF URL is short-lived or one hop away override `RescueCardSourceBase.ResolveDownloadUrlAsync` instead of resolving it during discovery.

**Splitting a combined multi-model PDF**: `split <brand|all>` cuts a brand's combined "all models" PDFs into one file per model. The brand-independent part (reading with `PdfPig`, copying page ranges into standalone PDFs with `PDFsharp`) is `Rettungskarten.Infrastructure/RescueCards/Splitting/CombinedPdfSplitter.cs`; how one brand's document maps pages to models is an `ICombinedPdfLayout` registered in `CombinedPdfLayouts.cs`. Two reusable bases cover the layouts seen so far: `OutlineCombinedPdfLayout` (one PDF bookmark per model) and `PageTextCombinedPdfLayout` (the model is recognizable from each page's own text — Porsche's layout, which has no bookmarks, groups pages by the document's own "ID no." footer). The combined entry is kept (`documentScope: combined`); each part becomes a `splitPart` entry, and re-running `split` replaces a document's previous parts. It's a separate command rather than part of `fetch`, so re-running it doesn't re-download the large source file — the same reasoning `prioritize` already follows as its own post-processing step.

**Data quality checks**: `inspect quality --rescue-cards-path <path>` (`Rettungskarten.Core/Quality/DataQualityChecker.cs`) checks already-fetched metadata for anomalies that a "did discovery return results" check can't catch — a `bodyType` that's actually a year (the shape of a real bug once found in Audi's parsing: a shifted field silently corrupted `BodyType`/`BuildYearFrom` for several cards), a `fuelType` that's just digits, a duplicate `id`, or one entire brand stuck at `bundlePriority: unknown` while others matched real stock data (the shape of the real Cupra/KBA brand-matching bug found this session — that last check only means anything once `prioritize` has actually run, so it won't fire from `link-check.yml`'s dry-run-only flow, only when run locally after the full `fetch` → `prioritize` pipeline). Since discovery already populates `bodyType`/`fuelType` even in `--dry-run` mode, the other checks run straight against dry-run output with no extra downloads — see the `link-check.yml` step below.

**Continuous integration** (`.github/workflows/`):

- `ci.yml` — build, test, and a `list brands` smoke test under both `en-US` and `de-DE` locales, on every push and PR.
- `link-check.yml` — a scheduled (weekly, Mondays) discovery-only run against every live source (`fetch rescue-cards --brand all --dry-run` plus `fetch stock`), followed by an `inspect quality` pass over the dry-run's output, so a manufacturer/KBA site change *or* a parsing regression is caught within a week instead of silently rotting; also runnable on demand from the Actions tab. See `CLAUDE.md` for what to do when adding a source that this workflow should also cover.

**Running tests**:

```bash
dotnet test
```

See `CLAUDE.md` for the project's contribution rules (English-only code/commits/PRs, build+test+review before committing, README maintenance).

### Known limitations

- **Porsche**: unlike every other brand, the source has no per-model file — Porsche publishes one combined PDF covering all current models (~55MB) plus a second for classic models, both served from Porsche's own CDN (`files.porsche.com`). `fetch rescue-cards --brand porsche` downloads those two files as-is (needing longer HTTP timeouts than the other brands, configured in `HttpServiceCollectionExtensions`); running `split porsche` afterwards splits them into one file per model by detecting model boundaries in the page text (grouped by the document's own "ID no." footer, since there's no PDF outline) and adds the per-model entries next to the two combined ones (which are kept, marked `documentScope: combined`). The document's actual language is English, not German (Porsche doesn't offer a separate German file here), and its content is in English (`languageCode: "EN"` on these entries, unlike every other brand). Model names/years are parsed heuristically from free text; a handful of entries where that parsing fails altogether fall back to the document's own internal ID as the "model name" (`parseConfidence: "unparsed"`) rather than being dropped. `bodyType` is also extracted from this free text (e.g. "Cabriolet", "SUV"); `doors`/`fuelType` are not, since Porsche's header text never states a door count and folds any fuel/drivetrain info (e.g. "E-Hybrid") into the model name itself rather than stating it separately.
- **robots.txt is respected**: every request is checked against the target host's `robots.txt` (RFC 9309 rules for this tool's product token `RettungskartenTool`, otherwise `*`) by `RobotsTxtDelegatingHandler`. A disallowed download is skipped with the failure reason "disallowed by the host's robots.txt" and the card is kept as metadata only — this affects the Stellantis brand sites that publish `Disallow: *.pdf$` (e.g. fiat.de). A robots.txt that can't be fetched (4xx) means no restrictions; a 5xx/network error is logged and treated the same, so a transient outage doesn't drop cards.
- **Bot protection (Akamai)**: several manufacturer sites (the Stellantis brand sites, Volvo, Tesla, Bentley, Ford's content CDN) answer non-browser requests with 403. Their sources use a separate `rettungskarten-browser` HTTP client that sends a browser's header set (User-Agent, `Accept-Language`, `Sec-Fetch-*`) and decompresses responses; robots.txt still applies to it. Verified on Windows; whether Akamai accepts it from the Linux CI runner (different TLS stack) is only known once `link-check.yml` has run there.
- **Cupra**: only the Swiss site is wired up; the Austrian site's downloads redirect to an identity/auth gateway.
- **Bentley**: each model's per-language PDF links use a *different* URL naming scheme depending on when that model's sheet was published (plain, spelled-out-language, `_Web` suffix, dated suffix, ...) - there's no single URL shape that reliably means "German". `BentleyRescueCardSource` filters on the link's own visible button label ("DEUTSCHE") instead, which is consistent across every scheme found.
- **Lamborghini**: like Porsche, the documents are English only - Lamborghini doesn't publish a separate German file.
- Rescue card filenames/link text are parsed heuristically (no brand publishes structured metadata) — see `ParseConfidence` on each entry. For VW/SEAT/Cupra this is the shared `StandardRescueSheetFilenameParser`; Audi gets its own `AudiFilenameParser` instead, since its CMS occasionally emits filenames the shared parser can't handle correctly (see that class's doc comment). Škoda's `bodyType`/`doors`/`fuelType` are likewise extracted from its model-page titles where stated (e.g. "Fabia Combi", "Citigo 3-Türer", "Octavia CNG") — only ~30% of titles state these explicitly, so most entries correctly leave them `null` rather than guessing.
- KBA's FZ12 file only lists model series with ≥1,000 registered vehicles (their own publication threshold); rarer models fall back to `bundlePriority: unknown`, which is the correct "fetch on demand" signal for this tool's purpose.
- FZ12 has no finer breakdown than "model series" - no split by generation, body type, or fuel type (verified against a real download: only a `Segment`/`Modellreihe`/`Anzahl` column layout, no others). So every rescue-card variant of one model (e.g. every Golf model year/body style) gets the *same* `estimatedFleetSize`/`bundlePriority` - there's no more granular KBA source to improve this with. `priority-report.json`/`.csv` accounts for this by listing one row per (brand, model) with a `cardCount` of how many rescue-card variants exist (and a `notDownloadedCount` of how many of those don't have a local PDF yet, so a "High priority" model with some failed downloads doesn't look fully covered), rather than repeating the same model with the same numbers once per card.
- KBA's FZ12 does not track Cupra as its own brand at all — every Cupra model is counted under "SEAT" instead (`BrandGroups.ParentBrandOf` makes SEAT Cupra's parent brand, so `BrandNames` matches Cupra cards against SEAT-labelled stock rows; the same mechanism covers Mercedes-AMG/-EQ/Maybach → MERCEDES, Vauxhall → OPEL and Fiat Professional/Abarth → FIAT). This means a Cupra card's `estimatedFleetSize` is the combined SEAT+Cupra registration count for that model name, not a Cupra-only figure — the best available approximation given KBA's granularity, not an exact count.
- Porsche's rescue cards use fine-grained per-generation/variant names (e.g. "911 Carrera", "Cayenne E-Hybrid") that don't match KBA's much coarser 9 tracked Porsche series (911, Boxster, Cayman, Cayenne, Macan, Panamera, Taycan, 928, 968) without help — `model-aliases.json` maps the known variants to their base KBA series. Genuinely untracked classics (356, 924, 944, 959, ...) correctly stay `bundlePriority: unknown`. Deliberately *not* aliased: ultra-low-volume specials (911 GT2/GT2 RS/GT3/GT3 RS/R/Turbo, 718 Cayman GT4) — mapping these to the base series' aggregate fleet count would overstate their real-world commonality far more than it would for an ordinary trim, so `unknown` (the honest "fetch on demand" signal) is more correct here than a falsely inflated priority.
- All JSON files (rescue-card sidecars, the KBA stock result/meta files) are written atomically (temp file + rename), so a process kill mid-write can't leave a truncated file behind. If a sidecar or stock file is nonetheless unreadable (e.g. external interference, or one written before this fix), it's skipped with a warning printed to stderr rather than aborting the whole command — for a rescue-card sidecar, only that one card is missing from the run's results until it's re-fetched or removed; for the single KBA stock result file, the whole `prioritize` run falls back to its usual "no stock data found" message, since there's nothing to partially recover from one file.

### Licensing note

KBA vehicle stock data (FZ12) is published under "Datenlizenz Deutschland – Namensnennung – Version 2.0" (attribution required) — this is recorded in every `fz12_<year>.meta.json` and `fz12_<year>.json`. Manufacturer rescue data sheets remain the property of their respective manufacturers; several manufacturer pages state the sheets are intended for trained rescue personnel specifically.

---

## Deutsch

### Was das hier ist

Eine .NET 10 Konsolenanwendung für deutsche Feuerwehren, die:

1. **Rettungsdatenblätter** ("Rettungskarten" — PDFs mit Airbag-Positionen, Schneidzonen und Kraftstoff-/Batterie-Lage für ein bestimmtes Fahrzeugmodell) für **Marken der Volkswagen-Gruppe** herunterlädt: VW, Audi, Škoda, SEAT, Cupra, Porsche, Bentley, Lamborghini.
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

`list brands` zeigt jederzeit den aktuellen Status.

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
    <marke>/_manifest.json       (abgeleitet, aggregiert alle Sidecars dieser Marke)
  stock/
    <jahr>/fz12_<jahr>.xlsx      (Rohdatei)
    <jahr>/fz12_<jahr>.json      (geparst)
    <jahr>/fz12_<jahr>.meta.json
  priority-report.json          (eine Zeile je Modell, sortiert nach geschätzter Bestandsgröße)
  priority-report.csv           (gleiche Daten, für Excel/Tabellenkalkulation)
```

`<marke>` ist der Markenname in Kleinbuchstaben wie bei `--brand` (z. B. `mercedesbenz`, `landrover`). Neben den geparsten Modelldaten hält jede Sidecar-Datei fest:

- `manufacturerGroup` — der Konzern (z. B. `volkswagenGroup`, `stellantis`, `independent`); auch eine Spalte in `priority-report.json`/`.csv`. smart ist die einzige Marke, deren Konzern von der Quelle abhängt (Mercedes-Benz bis 2021, Geely ab 2022).
- `documentScope` — `single` (ein Modell pro Datei), `combined` (eine Datei für viele Modelle, z. B. Porsches Alle-Modelle-PDF) oder `splitPart` (von `split` aus einer kombinierten Datei herausgeschnitten; `splitSourceId` verweist auf den kombinierten Eintrag, der erhalten bleibt).
- `chassisCode` — der Baureihen-/Plattformcode des Herstellers, sofern die Quelle ihn nennt (z. B. `W177`, `F45`), sonst `null`.

Sidecars, die vor diesen Feldern geschrieben wurden, werden mit den Standardwerten gelesen (`single`, Standardkonzern der Marke, kein Baureihencode).

### Für Entwickler

**Solution-Aufbau** (4 Projekte, `Rettungskarten.slnx`):

- `Rettungskarten.Core` — Domänenmodelle, Schnittstellen (`IRescueCardSource`, `IVehicleStockSource`, ...), Orchestrierung, Priorisierungslogik und die gemeinsamen Lokalisierungs-Strings. Keine I/O.
- `Rettungskarten.Infrastructure` — HTTP-Client-Setup (mit Rate-Limiting pro Host), AngleSharp-basiertes HTML-Parsing, ClosedXML-basiertes XLSX-Parsing, die acht Marken-Quellen, die KBA-Bestandsquelle und Dateisystem-Speicherung.
- `Rettungskarten.Cli` — Einstiegspunkt und Befehle auf Basis von `System.CommandLine`.
- `Rettungskarten.Tests` — xUnit-Tests, inklusive einer Regressionssuite, die den KBA-XLSX-Parser gegen eine echte heruntergeladene Datei prüft (`tests/Rettungskarten.Tests/Fixtures/fz12_2026.xlsx`). Jede Markenquelle (und die KBA-Bestandsquelle) hat einen Test auf `DiscoverAsync`-/`FetchWithRawAsync`-Ebene gegen eine Stub-`IHttpClientFactory` und eine echte, gekürzte HTML-/XLSX-Fixture — nicht nur die gemeinsamen Parser-Klassen für sich genommen —, da die Parser allein z. B. weder den VW-Sprach-Pfad-Bug noch den Audi-Feldverschiebungs-Bug aus dieser Session erkannt hätten.

**Neue Marke hinzufügen**: `Brand`-Enum-Wert ergänzen (allein dadurch ist sie gültiges `--brand`/`split`-Argument und Teil von `--brand all`), in `BrandGroups` zuordnen (Konzern und, falls das KBA ihre Fahrzeuge unter dem Namen einer anderen Marke zählt, Muttermarke) und, falls das KBA sie anders schreibt, in `BrandNames`; ihre `IRescueCardSource` in `Rettungskarten.Infrastructure/RescueCards/` implementieren und in `Rettungskarten.Cli/CompositionRoot.cs` registrieren (eine Marke darf mehrere Quellen haben — jede wird durchsucht, jeder Eintrag wird über die Quelle geladen, die ihn gefunden hat); `Brand_<Marke>_Source` (und, falls die Abdeckung von der deutschsprachigen Einzelmodell-Norm abweicht, `Brand_<Marke>_Status`) in beiden Ressourcendateien für `list brands` ergänzen. Die meisten Quellen bauen auf gemeinsamen Bausteinen auf: `HtmlPdfLinkRescueCardSource` (statische Seite(n) mit direkten PDF-Links), `StandardRescueSheetFilenameParser` (die branchenweite Konvention `Marke_Modell_Aufbau_Jahr_Türen_Kraftstoff_SPRACHE.pdf`), `RescueSheetLabelParser` (Freitext-Labels), `RescueDocumentClassifier` (sortiert ERGs, Legenden und Handbücher aus) und `LanguagePreference` (Deutsch, Englisch nur als Ersatz). `DiscoverAsync` darf niemals wegen Parsing-Problemen bei einem einzelnen Modell werfen; `DownloadAsync` muss bei erwarteten HTTP-Fehlern ein fehlgeschlagenes `RescueCardDownloadResult` zurückgeben statt zu werfen — der Orchestrator behandelt nur eine Exception aus `DiscoverAsync` als Markenfehler. Quellen, deren echte PDF-URL kurzlebig ist oder erst über einen Zwischenschritt erreichbar ist, überschreiben `RescueCardSourceBase.ResolveDownloadUrlAsync`, statt sie schon bei der Discovery aufzulösen.

**Eine kombinierte Multi-Modell-PDF aufteilen**: `split <marke|all>` schneidet die kombinierten Alle-Modelle-PDFs einer Marke in je eine Datei pro Modell. Der markenunabhängige Teil (Lesen mit `PdfPig`, Kopieren der Seitenbereiche in eigenständige PDFs mit `PDFsharp`) ist `Rettungskarten.Infrastructure/RescueCards/Splitting/CombinedPdfSplitter.cs`; wie das Dokument einer Marke Seiten auf Modelle abbildet, beschreibt ein in `CombinedPdfLayouts.cs` registriertes `ICombinedPdfLayout`. Zwei wiederverwendbare Basisklassen decken die bisher gefundenen Layouts ab: `OutlineCombinedPdfLayout` (ein PDF-Lesezeichen pro Modell) und `PageTextCombinedPdfLayout` (das Modell ist am Text jeder Seite erkennbar — Porsches Layout ohne Lesezeichen gruppiert Seiten über die dokumenteigene "ID no."-Fußzeile). Der kombinierte Eintrag bleibt erhalten (`documentScope: combined`); jedes Teil wird ein `splitPart`-Eintrag, und ein erneuter `split`-Lauf ersetzt die bisherigen Teile eines Dokuments. Das ist ein eigener Befehl statt Teil von `fetch`, damit ein erneuter Lauf nicht die große Quelldatei erneut herunterlädt — dieselbe Überlegung, die `prioritize` bereits als eigener Nachbearbeitungsschritt befolgt.

**Datenqualitäts-Prüfung**: `inspect quality --rescue-cards-path <pfad>` (`Rettungskarten.Core/Quality/DataQualityChecker.cs`) prüft bereits geladene Metadaten auf Auffälligkeiten, die eine reine „hat Discovery überhaupt Ergebnisse geliefert"-Prüfung nicht erkennt — eine `bodyType`, die eigentlich eine Jahreszahl ist (genau die Form eines echten Bugs, der einmal bei Audi gefunden wurde: ein verschobenes Feld hat `BodyType`/`BuildYearFrom` bei mehreren Karten stillschweigend verfälscht), eine `fuelType` aus reinen Ziffern, eine doppelte `id`, oder eine ganze Marke, die bei `bundlePriority: unknown` feststeckt, während andere Marken echte Bestandsdaten zugeordnet bekommen haben (genau die Form des echten Cupra/KBA-Markenabgleich-Bugs aus dieser Session — diese letzte Prüfung ergibt erst etwas, nachdem `prioritize` tatsächlich gelaufen ist, greift also nicht im reinen Dry-Run-Ablauf von `link-check.yml`, sondern nur bei lokaler Ausführung nach der vollständigen `fetch` → `prioritize`-Pipeline). Da Discovery `bodyType`/`fuelType` auch im `--dry-run`-Modus schon befüllt, laufen die übrigen Prüfungen direkt gegen die Dry-Run-Ausgabe, ohne zusätzliche Downloads — siehe den `link-check.yml`-Schritt unten.

**Continuous integration** (`.github/workflows/`):

- `ci.yml` — Build, Test und ein `list brands`-Smoke-Test unter den Locales `en-US` und `de-DE`, bei jedem Push und PR.
- `link-check.yml` — ein wöchentlich geplanter (montags), rein entdeckender Lauf gegen alle Live-Quellen (`fetch rescue-cards --brand all --dry-run` plus `fetch stock`), gefolgt von einem `inspect quality`-Durchlauf über die Dry-Run-Ausgabe, damit sowohl eine umgebaute/verschobene Herstellerseite als auch eine Parsing-Regression innerhalb einer Woche auffällt statt stillschweigend zu veralten; auch manuell über den Actions-Tab startbar. Was bei einer neuen Quelle zu tun ist, damit dieser Workflow sie mit abdeckt, steht in `CLAUDE.md`.

**Tests ausführen**:

```bash
dotnet test
```

Die Mitwirkungsregeln des Projekts (Code/Commits/PRs auf Englisch, Build+Test+Review vor jedem Commit, README-Pflege) stehen in `CLAUDE.md`.

### Bekannte Einschränkungen

- **Porsche**: anders als bei jeder anderen Marke hat die Quelle keine Datei pro Modell — Porsche veröffentlicht eine kombinierte PDF für alle aktuellen Modelle (~55MB) plus eine zweite für klassische Modelle, beide von Porsches eigenem CDN (`files.porsche.com`). `fetch rescue-cards --brand porsche` lädt diese zwei Dateien unverändert (benötigt längere HTTP-Timeouts als bei den anderen Marken, konfiguriert in `HttpServiceCollectionExtensions`); `split porsche` teilt sie anschließend anhand von Modellgrenzen im Seitentext (gruppiert über die dokumenteigene "ID no."-Fußzeile, da keine PDF-Gliederung existiert) in je eine Datei pro Modell auf und legt die Modell-Einträge neben den zwei kombinierten an (die als `documentScope: combined` erhalten bleiben). Die tatsächliche Sprache des Dokuments ist Englisch, nicht Deutsch (Porsche bietet hierfür keine eigene deutsche Datei an) - der Inhalt ist auf Englisch (`languageCode: "EN"` bei diesen Einträgen, anders als bei jeder anderen Marke). Modellnamen/-jahre werden heuristisch aus Fließtext geparst; einige wenige Einträge, bei denen das komplett fehlschlägt, fallen auf die dokumenteigene interne ID als "Modellname" zurück (`parseConfidence: "unparsed"`), statt verworfen zu werden. `bodyType` wird ebenfalls aus diesem Fließtext extrahiert (z. B. „Cabriolet", „SUV"); `doors`/`fuelType` nicht, da Porsches Header-Text nie eine Türzahl nennt und Antriebs-/Kraftstoffinformationen (z. B. „E-Hybrid") in den Modellnamen selbst einfließen, statt separat angegeben zu werden.
- **robots.txt wird respektiert**: jede Anfrage wird von `RobotsTxtDelegatingHandler` gegen die `robots.txt` des Ziel-Hosts geprüft (Regeln nach RFC 9309 für das Produkt-Token `RettungskartenTool`, sonst `*`). Ein gesperrter Download wird mit dem Fehlergrund „durch robots.txt des Hosts gesperrt“ übersprungen, die Karte bleibt als reiner Metadaten-Eintrag erhalten — das betrifft die Stellantis-Markenseiten mit `Disallow: *.pdf$` (z. B. fiat.de). Eine nicht abrufbare robots.txt (4xx) bedeutet keine Einschränkungen; ein 5xx-/Netzwerkfehler wird protokolliert und genauso behandelt, damit ein kurzer Ausfall keine Karten kostet.
- **Bot-Schutz (Akamai)**: mehrere Herstellerseiten (die Stellantis-Markenseiten, Volvo, Tesla, Bentley, Fords Content-CDN) beantworten Anfragen ohne Browser-Merkmale mit 403. Ihre Quellen nutzen einen eigenen HTTP-Client `rettungskarten-browser`, der die Header eines Browsers sendet (User-Agent, `Accept-Language`, `Sec-Fetch-*`) und Antworten dekomprimiert; robots.txt gilt auch für ihn. Unter Windows verifiziert; ob Akamai ihn auch vom Linux-CI-Runner (anderer TLS-Stack) akzeptiert, zeigt erst ein Lauf von `link-check.yml`.
- **Cupra**: nur die Schweiz-Seite ist angebunden; die Downloads der österreichischen Seite leiten auf ein Identity-/Auth-Gateway um.
- **Bentley**: die Pro-Sprache-PDF-Links jedes Modells verwenden je nach Veröffentlichungszeitpunkt ein *anderes* URL-Namensschema (schlicht, ausgeschriebene Sprache, `_Web`-Suffix, datiertes Suffix, ...) — es gibt kein einheitliches URL-Muster, das zuverlässig „Deutsch" bedeutet. `BentleyRescueCardSource` filtert stattdessen auf das sichtbare Button-Label des Links („DEUTSCHE"), das über alle gefundenen Schemata hinweg konsistent ist.
- **Lamborghini**: wie bei Porsche sind die Dokumente nur auf Englisch — Lamborghini veröffentlicht hierfür keine eigene deutsche Datei.
- Dateinamen/Linktexte der Rettungskarten werden heuristisch geparst (keine Marke veröffentlicht strukturierte Metadaten) — siehe `ParseConfidence` je Eintrag. Bei VW/SEAT/Cupra übernimmt das der gemeinsame `StandardRescueSheetFilenameParser`; Audi hat einen eigenen `AudiFilenameParser`, da dessen CMS gelegentlich Dateinamen erzeugt, die der gemeinsame Parser nicht korrekt verarbeiten kann (siehe Doc-Kommentar dieser Klasse). Auch Škodas `bodyType`/`doors`/`fuelType` werden aus den Modellseiten-Titeln extrahiert, wo angegeben (z. B. „Fabia Combi", „Citigo 3-Türer", „Octavia CNG") — nur ca. 30 % der Titel geben das explizit an, der Rest bleibt korrekterweise `null` statt geraten zu werden.
- Die FZ12-Datei des KBA listet nur Modellreihen mit ≥1.000 zugelassenen Fahrzeugen (deren eigene Veröffentlichungsschwelle); seltenere Modelle fallen auf `bundlePriority: unknown` zurück — genau das richtige "nur auf Abruf"-Signal für den Zweck dieses Werkzeugs.
- FZ12 hat keine feinere Aufschlüsselung als „Modellreihe" — keine Aufteilung nach Generation, Karosserieform oder Kraftstoffart (an einem echten Download verifiziert: nur die Spalten `Segment`/`Modellreihe`/`Anzahl`, keine weiteren). Jede Rettungskarten-Variante eines Modells (z. B. jeder Golf-Modelljahrgang/jede Karosserieform) bekommt daher dieselbe `estimatedFleetSize`/`bundlePriority` — eine feinere KBA-Quelle dafür gibt es nicht. `priority-report.json`/`.csv` trägt dem Rechnung, indem eine Zeile je (Marke, Modell) mit einer `cardCount` (Anzahl der Rettungskarten-Varianten) und einer `notDownloadedCount` (davon ohne lokale PDF) aufgeführt wird — so wirkt ein Modell mit „Hoch"-Priorität nicht fälschlich vollständig abgedeckt, wenn einzelne Downloads fehlgeschlagen sind —, statt dasselbe Modell mit denselben Zahlen einmal je Karte zu wiederholen.
- Die FZ12-Datei des KBA führt Cupra überhaupt nicht als eigene Marke — jedes Cupra-Modell wird stattdessen unter „SEAT" gezählt (`BrandGroups.ParentBrandOf` macht SEAT zur Muttermarke von Cupra, sodass `BrandNames` Cupra-Karten gegen SEAT-beschriftete Bestandszeilen abgleicht; derselbe Mechanismus deckt Mercedes-AMG/-EQ/Maybach → MERCEDES, Vauxhall → OPEL und Fiat Professional/Abarth → FIAT ab). Das bedeutet: `estimatedFleetSize` einer Cupra-Karte ist die kombinierte SEAT+Cupra-Zulassungszahl für dieses Modell, keine reine Cupra-Zahl — die bestmögliche Näherung angesichts der KBA-Granularität, keine exakte Zählung.
- Porsches Rettungskarten verwenden feingranulare Generations-/Varianten-Namen (z. B. „911 Carrera", „Cayenne E-Hybrid"), die ohne Weiteres nicht zu den deutlich gröberen 9 vom KBA erfassten Porsche-Baureihen passen (911, Boxster, Cayman, Cayenne, Macan, Panamera, Taycan, 928, 968) — `model-aliases.json` ordnet die bekannten Varianten ihrer jeweiligen KBA-Basisbaureihe zu. Echte, vom KBA nicht erfasste Klassiker (356, 924, 944, 959, ...) bleiben zurecht bei `bundlePriority: unknown`. Bewusst *nicht* zugeordnet: extrem seltene Spezialmodelle (911 GT2/GT2 RS/GT3/GT3 RS/R/Turbo, 718 Cayman GT4) — sie der aggregierten Bestandszahl der Basisbaureihe zuzuordnen würde ihre tatsächliche Verbreitung stärker verzerren als bei einer gewöhnlichen Ausstattungsvariante, daher ist `unknown` (das ehrliche „nur auf Abruf"-Signal) hier korrekter als eine fälschlich zu hohe Priorität.
- Alle JSON-Dateien (Rettungskarten-Sidecars, die KBA-Bestandsergebnis-/Meta-Dateien) werden atomar geschrieben (Temp-Datei + Umbenennen), sodass ein Prozessabbruch mitten im Schreiben keine abgeschnittene Datei hinterlassen kann. Ist eine Sidecar- oder Bestandsdatei trotzdem nicht lesbar (z. B. externe Einwirkung oder vor diesem Fix geschrieben), wird sie mit einer Warnung auf stderr übersprungen, statt den gesamten Befehl abzubrechen — bei einer Rettungskarten-Sidecar fehlt dadurch nur diese eine Karte im Ergebnis des Laufs, bis sie erneut geladen oder entfernt wird; bei der einzelnen KBA-Bestandsergebnisdatei fällt der gesamte `prioritize`-Lauf auf die übliche „kein Bestand gefunden"-Meldung zurück, da es bei nur einer Datei nichts teilweise zu retten gibt.

### Hinweis zur Lizenzierung

KBA-Fahrzeugbestandsdaten (FZ12) werden unter "Datenlizenz Deutschland – Namensnennung – Version 2.0" veröffentlicht (Namensnennung erforderlich) — das wird in jeder `fz12_<jahr>.meta.json` und `fz12_<jahr>.json` festgehalten. Die Rettungsdatenblätter der Hersteller bleiben Eigentum der jeweiligen Hersteller; mehrere Herstellerseiten weisen ausdrücklich darauf hin, dass die Blätter für geschultes Rettungspersonal bestimmt sind.
