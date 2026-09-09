# Rettungskarten & Vehicle Stock Tool

**Languages:** [🇬🇧 English](#english) | [🇩🇪 Deutsch](#deutsch)

---

## English

### What this is

A .NET 10 console application for German fire brigades that:

1. Downloads **rescue data sheets** ("Rettungskarten"/"Rettungsdatenblätter" — PDFs showing airbag locations, cut zones, and fuel/battery placement for a specific vehicle model) for **Volkswagen Group brands**: VW, Audi, Škoda, SEAT, Cupra, Porsche.
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
| Porsche | Fully functional. Source is 2 combined PDFs (current + classic models); `fetch` downloads them as-is, then `split porsche` splits them into one file per model, same as every other brand | `porsche.com` official documents page (Vue SSR template embedded in a `<script type="text/x-template">` block) |

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

# Split Porsche's combined all-models PDF(s) (already fetched above) into one file per model
dotnet run --project src/Rettungskarten.Cli -- split porsche

# List brand implementation status
dotnet run --project src/Rettungskarten.Cli -- list brands

# Developer tool: inspect an XLSX file's schema
dotnet run --project src/Rettungskarten.Cli -- inspect xlsx path/to/file.xlsx
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
  priority-report.json
```

### For developers

**Solution layout** (4 projects, `Rettungskarten.slnx`):

- `Rettungskarten.Core` — domain models, interfaces (`IRescueCardSource`, `IVehicleStockSource`, ...), orchestration, priority-matching logic, and the shared localization strings. No I/O.
- `Rettungskarten.Infrastructure` — HTTP client setup (with per-host rate limiting), AngleSharp-based HTML parsing, ClosedXML-based XLSX parsing, the six brand sources, the KBA stock source, and file-system storage.
- `Rettungskarten.Cli` — `System.CommandLine`-based entry point and command implementations.
- `Rettungskarten.Tests` — xUnit tests, including a regression suite that runs the KBA XLSX parser against a real downloaded file (`tests/Rettungskarten.Tests/Fixtures/fz12_2026.xlsx`).

**Adding a new brand**: implement `IRescueCardSource` in `Rettungskarten.Infrastructure/RescueCards/`, register it in `Rettungskarten.Cli/CompositionRoot.cs`, and add it to the `Brand` enum and `ListBrandsCommand`. `DiscoverAsync` must never throw for a single model's parsing trouble; `DownloadAsync` must return a failed `RescueCardDownloadResult` rather than throwing for expected HTTP failures — the orchestrator only treats an exception from `DiscoverAsync` as a brand-level failure.

**Splitting a combined multi-model PDF** (Porsche's case, and a template if another brand ever turns out to work the same way): `Rettungskarten.Infrastructure/RescueCards/Splitting/PorscheCombinedPdfSplitter.cs` uses `PdfPig` to find model boundaries in the page text (no PDF outline/bookmarks exist in Porsche's file) and `PDFsharp` to copy the matched page ranges into standalone PDFs. It's invoked by the separate `split porsche` command rather than by `fetch` itself, so re-running it doesn't re-download the large source file — the same reasoning `prioritize` already follows as its own post-processing step.

**Continuous integration** (`.github/workflows/`):

- `ci.yml` — build, test, and a `list brands` smoke test under both `en-US` and `de-DE` locales, on every push and PR.
- `link-check.yml` — a scheduled (weekly, Mondays) discovery-only run against every live source (`fetch rescue-cards --brand all --dry-run` plus `fetch stock`), so a manufacturer or KBA moving/restructuring a page is caught within a week instead of silently rotting; also runnable on demand from the Actions tab. See `CLAUDE.md` for what to do when adding a source that this workflow should also cover.

**Running tests**:

```bash
dotnet test
```

See `CLAUDE.md` for the project's contribution rules (English-only code/commits/PRs, build+test+review before committing, README maintenance).

### Known limitations

- **Porsche**: unlike every other brand, the source has no per-model file — Porsche publishes one combined PDF covering all current models (~55MB) plus a second for classic models, both served from Porsche's own CDN (`assets-v2.porsche.com`). `fetch rescue-cards --brand porsche` downloads those two files as-is (needing longer HTTP timeouts than the other brands, configured in `PoliteHttpClientFactory`); running `split porsche` afterwards splits them into one file per model by detecting model boundaries in the page text (grouped by the document's own "ID no." footer, since there's no PDF outline) and replaces the two combined entries with the per-model ones. The document's actual language is English, not German (Porsche doesn't offer a separate German file here), and its content is in English (`languageCode: "EN"` on these entries, unlike every other brand). Model names/years are parsed heuristically from free text; a handful of entries where that parsing fails altogether fall back to the document's own internal ID as the "model name" (`parseConfidence: "unparsed"`) rather than being dropped. `bodyType` is also extracted from this free text (e.g. "Cabriolet", "SUV"); `doors`/`fuelType` are not, since Porsche's header text never states a door count and folds any fuel/drivetrain info (e.g. "E-Hybrid") into the model name itself rather than stating it separately.
- **Cupra**: only the Swiss site is wired up; the Austrian site's downloads redirect to an identity/auth gateway.
- Rescue card filenames/link text are parsed heuristically (no brand publishes structured metadata) — see `ParseConfidence` on each entry. For VW/SEAT/Cupra this is the shared `VwSeatCupraFilenameParser`; Audi gets its own `AudiFilenameParser` instead, since its CMS occasionally emits filenames the shared parser can't handle correctly (see that class's doc comment). Škoda's `bodyType`/`doors`/`fuelType` are likewise extracted from its model-page titles where stated (e.g. "Fabia Combi", "Citigo 3-Türer", "Octavia CNG") — only ~30% of titles state these explicitly, so most entries correctly leave them `null` rather than guessing.
- KBA's FZ12 file only lists model series with ≥1,000 registered vehicles (their own publication threshold); rarer models fall back to `bundlePriority: unknown`, which is the correct "fetch on demand" signal for this tool's purpose.
- KBA's FZ12 does not track Cupra as its own brand at all — every Cupra model is counted under "SEAT" instead (`BrandNames` matches Cupra cards against SEAT-labelled stock rows to account for this). This means a Cupra card's `estimatedFleetSize` is the combined SEAT+Cupra registration count for that model name, not a Cupra-only figure — the best available approximation given KBA's granularity, not an exact count.
- Porsche's rescue cards use fine-grained per-generation/variant names (e.g. "911 Carrera", "Cayenne E-Hybrid") that don't match KBA's much coarser 9 tracked Porsche series (911, Boxster, Cayman, Cayenne, Macan, Panamera, Taycan, 928, 968) without help — `model-aliases.json` maps the known variants to their base KBA series. Genuinely untracked classics (356, 924, 944, 959, ...) correctly stay `bundlePriority: unknown`. Deliberately *not* aliased: ultra-low-volume specials (911 GT2/GT2 RS/GT3/GT3 RS/R/Turbo, 718 Cayman GT4) — mapping these to the base series' aggregate fleet count would overstate their real-world commonality far more than it would for an ordinary trim, so `unknown` (the honest "fetch on demand" signal) is more correct here than a falsely inflated priority.

### Licensing note

KBA vehicle stock data (FZ12) is published under "Datenlizenz Deutschland – Namensnennung – Version 2.0" (attribution required) — this is recorded in every `fz12_<year>.meta.json` and `fz12_<year>.json`. Manufacturer rescue data sheets remain the property of their respective manufacturers; several manufacturer pages state the sheets are intended for trained rescue personnel specifically.

---

## Deutsch

### Was das hier ist

Eine .NET 10 Konsolenanwendung für deutsche Feuerwehren, die:

1. **Rettungsdatenblätter** ("Rettungskarten" — PDFs mit Airbag-Positionen, Schneidzonen und Kraftstoff-/Batterie-Lage für ein bestimmtes Fahrzeugmodell) für **Marken der Volkswagen-Gruppe** herunterlädt: VW, Audi, Škoda, SEAT, Cupra, Porsche.
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
| Porsche | Voll funktionsfähig. Quelle sind 2 kombinierte PDFs (aktuelle + klassische Modelle); `fetch` lädt sie unverändert, `split porsche` teilt sie anschließend in je eine Datei pro Modell auf, wie bei jeder anderen Marke | `porsche.com` offizielle Dokumente-Seite (Vue-SSR-Template eingebettet in einem `<script type="text/x-template">`-Block) |

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

# Porsches kombinierte Alle-Modelle-PDF(s) (zuvor geladen) in je eine Datei pro Modell aufteilen
dotnet run --project src/Rettungskarten.Cli -- split porsche

# Implementierungsstatus der Marken auflisten
dotnet run --project src/Rettungskarten.Cli -- list brands

# Entwicklerwerkzeug: Schema einer XLSX-Datei prüfen
dotnet run --project src/Rettungskarten.Cli -- inspect xlsx pfad/zur/datei.xlsx
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
  priority-report.json
```

### Für Entwickler

**Solution-Aufbau** (4 Projekte, `Rettungskarten.slnx`):

- `Rettungskarten.Core` — Domänenmodelle, Schnittstellen (`IRescueCardSource`, `IVehicleStockSource`, ...), Orchestrierung, Priorisierungslogik und die gemeinsamen Lokalisierungs-Strings. Keine I/O.
- `Rettungskarten.Infrastructure` — HTTP-Client-Setup (mit Rate-Limiting pro Host), AngleSharp-basiertes HTML-Parsing, ClosedXML-basiertes XLSX-Parsing, die sechs Marken-Quellen, die KBA-Bestandsquelle und Dateisystem-Speicherung.
- `Rettungskarten.Cli` — Einstiegspunkt und Befehle auf Basis von `System.CommandLine`.
- `Rettungskarten.Tests` — xUnit-Tests, inklusive einer Regressionssuite, die den KBA-XLSX-Parser gegen eine echte heruntergeladene Datei prüft (`tests/Rettungskarten.Tests/Fixtures/fz12_2026.xlsx`).

**Neue Marke hinzufügen**: `IRescueCardSource` in `Rettungskarten.Infrastructure/RescueCards/` implementieren, in `Rettungskarten.Cli/CompositionRoot.cs` registrieren, im `Brand`-Enum und in `ListBrandsCommand` ergänzen. `DiscoverAsync` darf niemals wegen Parsing-Problemen bei einem einzelnen Modell werfen; `DownloadAsync` muss bei erwarteten HTTP-Fehlern ein fehlgeschlagenes `RescueCardDownloadResult` zurückgeben statt zu werfen — der Orchestrator behandelt nur eine Exception aus `DiscoverAsync` als Markenfehler.

**Eine kombinierte Multi-Modell-PDF aufteilen** (Porsches Fall, als Vorlage falls eine andere Marke sich je genauso verhält): `Rettungskarten.Infrastructure/RescueCards/Splitting/PorscheCombinedPdfSplitter.cs` nutzt `PdfPig`, um Modellgrenzen im Seitentext zu finden (Porsches Datei hat keine PDF-Bookmarks/Gliederung), und `PDFsharp`, um die passenden Seitenbereiche in eigenständige PDFs zu kopieren. Aufgerufen wird das über den separaten `split porsche`-Befehl statt direkt durch `fetch`, damit ein erneuter Lauf nicht die große Quelldatei erneut herunterlädt — dieselbe Überlegung, die `prioritize` bereits als eigener Nachbearbeitungsschritt befolgt.

**Continuous integration** (`.github/workflows/`):

- `ci.yml` — Build, Test und ein `list brands`-Smoke-Test unter den Locales `en-US` und `de-DE`, bei jedem Push und PR.
- `link-check.yml` — ein wöchentlich geplanter (montags), rein entdeckender Lauf gegen alle Live-Quellen (`fetch rescue-cards --brand all --dry-run` plus `fetch stock`), damit eine von Hersteller oder KBA umgebaute/verschobene Seite innerhalb einer Woche auffällt statt stillschweigend zu veralten; auch manuell über den Actions-Tab startbar. Was bei einer neuen Quelle zu tun ist, damit dieser Workflow sie mit abdeckt, steht in `CLAUDE.md`.

**Tests ausführen**:

```bash
dotnet test
```

Die Mitwirkungsregeln des Projekts (Code/Commits/PRs auf Englisch, Build+Test+Review vor jedem Commit, README-Pflege) stehen in `CLAUDE.md`.

### Bekannte Einschränkungen

- **Porsche**: anders als bei jeder anderen Marke hat die Quelle keine Datei pro Modell — Porsche veröffentlicht eine kombinierte PDF für alle aktuellen Modelle (~55MB) plus eine zweite für klassische Modelle, beide von Porsches eigenem CDN (`assets-v2.porsche.com`). `fetch rescue-cards --brand porsche` lädt diese zwei Dateien unverändert (benötigt längere HTTP-Timeouts als bei den anderen Marken, konfiguriert in `PoliteHttpClientFactory`); `split porsche` teilt sie anschließend anhand von Modellgrenzen im Seitentext (gruppiert über die dokumenteigene "ID no."-Fußzeile, da keine PDF-Gliederung existiert) in je eine Datei pro Modell auf und ersetzt die zwei kombinierten Einträge durch die Modell-Einträge. Die tatsächliche Sprache des Dokuments ist Englisch, nicht Deutsch (Porsche bietet hierfür keine eigene deutsche Datei an) - der Inhalt ist auf Englisch (`languageCode: "EN"` bei diesen Einträgen, anders als bei jeder anderen Marke). Modellnamen/-jahre werden heuristisch aus Fließtext geparst; einige wenige Einträge, bei denen das komplett fehlschlägt, fallen auf die dokumenteigene interne ID als "Modellname" zurück (`parseConfidence: "unparsed"`), statt verworfen zu werden. `bodyType` wird ebenfalls aus diesem Fließtext extrahiert (z. B. „Cabriolet", „SUV"); `doors`/`fuelType` nicht, da Porsches Header-Text nie eine Türzahl nennt und Antriebs-/Kraftstoffinformationen (z. B. „E-Hybrid") in den Modellnamen selbst einfließen, statt separat angegeben zu werden.
- **Cupra**: nur die Schweiz-Seite ist angebunden; die Downloads der österreichischen Seite leiten auf ein Identity-/Auth-Gateway um.
- Dateinamen/Linktexte der Rettungskarten werden heuristisch geparst (keine Marke veröffentlicht strukturierte Metadaten) — siehe `ParseConfidence` je Eintrag. Bei VW/SEAT/Cupra übernimmt das der gemeinsame `VwSeatCupraFilenameParser`; Audi hat einen eigenen `AudiFilenameParser`, da dessen CMS gelegentlich Dateinamen erzeugt, die der gemeinsame Parser nicht korrekt verarbeiten kann (siehe Doc-Kommentar dieser Klasse). Auch Škodas `bodyType`/`doors`/`fuelType` werden aus den Modellseiten-Titeln extrahiert, wo angegeben (z. B. „Fabia Combi", „Citigo 3-Türer", „Octavia CNG") — nur ca. 30 % der Titel geben das explizit an, der Rest bleibt korrekterweise `null` statt geraten zu werden.
- Die FZ12-Datei des KBA listet nur Modellreihen mit ≥1.000 zugelassenen Fahrzeugen (deren eigene Veröffentlichungsschwelle); seltenere Modelle fallen auf `bundlePriority: unknown` zurück — genau das richtige "nur auf Abruf"-Signal für den Zweck dieses Werkzeugs.
- Die FZ12-Datei des KBA führt Cupra überhaupt nicht als eigene Marke — jedes Cupra-Modell wird stattdessen unter „SEAT" gezählt (`BrandNames` gleicht Cupra-Karten deshalb gegen SEAT-beschriftete Bestandszeilen ab). Das bedeutet: `estimatedFleetSize` einer Cupra-Karte ist die kombinierte SEAT+Cupra-Zulassungszahl für dieses Modell, keine reine Cupra-Zahl — die bestmögliche Näherung angesichts der KBA-Granularität, keine exakte Zählung.
- Porsches Rettungskarten verwenden feingranulare Generations-/Varianten-Namen (z. B. „911 Carrera", „Cayenne E-Hybrid"), die ohne Weiteres nicht zu den deutlich gröberen 9 vom KBA erfassten Porsche-Baureihen passen (911, Boxster, Cayman, Cayenne, Macan, Panamera, Taycan, 928, 968) — `model-aliases.json` ordnet die bekannten Varianten ihrer jeweiligen KBA-Basisbaureihe zu. Echte, vom KBA nicht erfasste Klassiker (356, 924, 944, 959, ...) bleiben zurecht bei `bundlePriority: unknown`. Bewusst *nicht* zugeordnet: extrem seltene Spezialmodelle (911 GT2/GT2 RS/GT3/GT3 RS/R/Turbo, 718 Cayman GT4) — sie der aggregierten Bestandszahl der Basisbaureihe zuzuordnen würde ihre tatsächliche Verbreitung stärker verzerren als bei einer gewöhnlichen Ausstattungsvariante, daher ist `unknown` (das ehrliche „nur auf Abruf"-Signal) hier korrekter als eine fälschlich zu hohe Priorität.

### Hinweis zur Lizenzierung

KBA-Fahrzeugbestandsdaten (FZ12) werden unter "Datenlizenz Deutschland – Namensnennung – Version 2.0" veröffentlicht (Namensnennung erforderlich) — das wird in jeder `fz12_<jahr>.meta.json` und `fz12_<jahr>.json` festgehalten. Die Rettungsdatenblätter der Hersteller bleiben Eigentum der jeweiligen Hersteller; mehrere Herstellerseiten weisen ausdrücklich darauf hin, dass die Blätter für geschultes Rettungspersonal bestimmt sind.
