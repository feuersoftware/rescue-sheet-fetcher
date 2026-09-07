# Rettungskarten & Vehicle Stock Tool

**Languages:** [🇬🇧 English](#english) | [🇩🇪 Deutsch](#deutsch)

---

## English

### What this is

A .NET 10 console application for German fire brigades that:

1. Downloads **rescue data sheets** ("Rettungskarten"/"Rettungsdatenblätter" — PDFs showing airbag locations, cut zones, and fuel/battery placement for a specific vehicle model) for **Volkswagen Group brands**: VW, Audi, Škoda, SEAT, Cupra. Porsche has no confirmed public source and is registered as a stub.
2. Downloads the **KBA vehicle stock statistic (FZ12)** — how many vehicles of each model series are actually registered in Germany — and uses it to **prioritize** which rescue cards are common enough to bundle directly into a field app vs. only fetch on demand.
3. Stores everything in a predictable folder structure on disk, with a JSON metadata sidecar per rescue card (build year, model, manufacturer, sibling/platform-sharing models, estimated fleet size, bundle priority).

No manufacturer publishes a stable official API for this — every brand's page structure had to be reverse-engineered individually. See the per-brand status table below and the code comments in `src/Rettungskarten.Infrastructure/RescueCards/` for what was found and why some sources are more reliable than others.

### Supported brands

| Brand | Status | Source |
|---|---|---|
| VW | Discovery works, PDF download blocked with HTTP 403 (unresolved, see code comments) | JSON feed on `assets.feature-app.io` |
| Audi | Fully functional | Static HTML page on `audi.com` |
| Škoda | Fully functional | Model pages on `skoda-auto.de` |
| SEAT | Fully functional | Model pages on `seat.de` |
| Cupra | Fully functional (Swiss site only; the Austrian site's downloads are gated behind an auth redirect) | Static HTML page on `cupraofficial.ch` |
| Porsche | Not implemented | No confirmed public source found |

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

**Running tests**:

```bash
dotnet test
```

See `CLAUDE.md` for the project's contribution rules (English-only code/commits/PRs, build+test+review before committing, README maintenance).

### Known limitations

- **VW**: rescue-card metadata is fully discovered, but the actual PDF download returns HTTP 403 (likely a signed-URL or session requirement not yet reverse-engineered). Cards are stored with `status: metadataOnly`.
- **Porsche**: no public source found; registered as a stub that reports "not implemented" without blocking other brands.
- **Cupra**: only the Swiss site is wired up; the Austrian site's downloads redirect to an identity/auth gateway.
- Rescue card filenames/link text are parsed heuristically (no brand publishes structured metadata) — see `ParseConfidence` on each entry.
- KBA's FZ12 file only lists model series with ≥1,000 registered vehicles (their own publication threshold); rarer models fall back to `bundlePriority: unknown`, which is the correct "fetch on demand" signal for this tool's purpose.

### Licensing note

KBA vehicle stock data (FZ12) is published under "Datenlizenz Deutschland – Namensnennung – Version 2.0" (attribution required) — this is recorded in every `fz12_<year>.meta.json` and `fz12_<year>.json`. Manufacturer rescue data sheets remain the property of their respective manufacturers; several manufacturer pages state the sheets are intended for trained rescue personnel specifically.

---

## Deutsch

### Was das hier ist

Eine .NET 10 Konsolenanwendung für deutsche Feuerwehren, die:

1. **Rettungsdatenblätter** ("Rettungskarten" — PDFs mit Airbag-Positionen, Schneidzonen und Kraftstoff-/Batterie-Lage für ein bestimmtes Fahrzeugmodell) für **Marken der Volkswagen-Gruppe** herunterlädt: VW, Audi, Škoda, SEAT, Cupra. Porsche hat keine bestätigte öffentliche Quelle und ist als Stub registriert.
2. Die **KBA-Fahrzeugbestandsstatistik (FZ12)** herunterlädt — wie viele Fahrzeuge jeder Modellreihe tatsächlich in Deutschland zugelassen sind — und damit **priorisiert**, welche Rettungskarten verbreitet genug sind, um direkt in eine Einsatz-App gebündelt zu werden, statt nur auf Abruf verfügbar zu sein.
3. Alles in einer vorhersehbaren Ordnerstruktur auf der Festplatte ablegt, mit einer JSON-Metadaten-Datei je Rettungskarte (Baujahr, Modell, Hersteller, Schwestermodelle/Plattform-Geschwister, geschätzte Bestandsgröße, Bündel-Priorität).

Kein Hersteller veröffentlicht dafür eine stabile offizielle API — die Seitenstruktur jeder Marke musste einzeln reverse-engineered werden. Siehe die Marken-Statustabelle unten und die Code-Kommentare in `src/Rettungskarten.Infrastructure/RescueCards/` für Details zu den Rechercheergebnissen.

### Unterstützte Marken

| Marke | Status | Quelle |
|---|---|---|
| VW | Discovery funktioniert, PDF-Download mit HTTP 403 blockiert (ungeklärt, siehe Code-Kommentare) | JSON-Feed auf `assets.feature-app.io` |
| Audi | Voll funktionsfähig | Statische HTML-Seite auf `audi.com` |
| Škoda | Voll funktionsfähig | Modellseiten auf `skoda-auto.de` |
| SEAT | Voll funktionsfähig | Modellseiten auf `seat.de` |
| Cupra | Voll funktionsfähig (nur Schweiz-Seite; Downloads der österreichischen Seite sind hinter einem Auth-Redirect) | Statische HTML-Seite auf `cupraofficial.ch` |
| Porsche | Nicht implementiert | Keine bestätigte öffentliche Quelle gefunden |

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

**Tests ausführen**:

```bash
dotnet test
```

Die Mitwirkungsregeln des Projekts (Code/Commits/PRs auf Englisch, Build+Test+Review vor jedem Commit, README-Pflege) stehen in `CLAUDE.md`.

### Bekannte Einschränkungen

- **VW**: Rettungskarten-Metadaten werden vollständig entdeckt, aber der eigentliche PDF-Download liefert HTTP 403 (vermutlich eine signierte URL oder Session-Anforderung, die noch nicht reverse-engineered wurde). Karten werden mit `status: metadataOnly` gespeichert.
- **Porsche**: keine öffentliche Quelle gefunden; als Stub registriert, meldet "nicht implementiert", ohne andere Marken zu blockieren.
- **Cupra**: nur die Schweiz-Seite ist angebunden; die Downloads der österreichischen Seite leiten auf ein Identity-/Auth-Gateway um.
- Dateinamen/Linktexte der Rettungskarten werden heuristisch geparst (keine Marke veröffentlicht strukturierte Metadaten) — siehe `ParseConfidence` je Eintrag.
- Die FZ12-Datei des KBA listet nur Modellreihen mit ≥1.000 zugelassenen Fahrzeugen (deren eigene Veröffentlichungsschwelle); seltenere Modelle fallen auf `bundlePriority: unknown` zurück — genau das richtige "nur auf Abruf"-Signal für den Zweck dieses Werkzeugs.

### Hinweis zur Lizenzierung

KBA-Fahrzeugbestandsdaten (FZ12) werden unter "Datenlizenz Deutschland – Namensnennung – Version 2.0" veröffentlicht (Namensnennung erforderlich) — das wird in jeder `fz12_<jahr>.meta.json` und `fz12_<jahr>.json` festgehalten. Die Rettungsdatenblätter der Hersteller bleiben Eigentum der jeweiligen Hersteller; mehrere Herstellerseiten weisen ausdrücklich darauf hin, dass die Blätter für geschultes Rettungspersonal bestimmt sind.
