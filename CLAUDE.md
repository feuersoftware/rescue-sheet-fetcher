# CLAUDE.md

Project-specific rules for Claude Code when working in this repository. These apply in addition to
the general engineering defaults — they are not a replacement for judgment, but they are not optional
either: follow them for every change in this repo unless the user explicitly says otherwise for that
one instance.

## Keep the README up to date

`README.md` is bilingual (English + German) and covers both users and developers. Whenever a change
affects anything it documents — CLI commands/options, supported brands, on-disk output layout, solution
structure, known limitations, localization keys — update **both** the English and German sections in
the same change. Do not let the two language sections drift out of sync with each other or with the
actual behavior of the app.

## Keep the weekly link check current

`.github/workflows/link-check.yml` runs a scheduled, discovery-only check against every live source
this app depends on — all rescue-card brand sources (`fetch rescue-cards --brand all --dry-run`) and
the KBA stock source (`fetch stock`) — so a manufacturer moving or restructuring a page gets caught
within a week instead of silently rotting until someone runs the app and wonders why discovery came
back empty. Whenever you add a new *kind* of external source this CLI fetches or discovers over the
network:

- A new rescue-card brand (a new `IRescueCardSource` + `Brand` value) needs **no workflow change** —
  `--brand all` iterates the `Brand` enum, so it's covered automatically once the source is
  registered in `CompositionRoot.cs`.
- Anything not already exercised by one of the workflow's existing steps (a new stock/data feed, a
  new CLI subcommand that resolves its own external URL) needs a **new step added to
  `link-check.yml`** that exercises it — prefer a discovery-only/dry-run path over a full download so
  the job stays fast and polite to source servers, following the pattern of the existing steps.

A brand or stock source's *entry-point URL* changing is exactly what this workflow exists to catch —
that never needs a workflow edit, only a code fix once the check goes red.

## Code, commits, and PRs are in English

- All code: identifiers, comments, exception messages in source, log message *templates* (the
  `Strings.resx` English values), commit messages, and PR titles/descriptions are written in English.
- User-facing *runtime* text is the one deliberate exception: it is localized (English + German) via
  `Rettungskarten.Core/Localization/Strings.resx` / `Strings.de.resx` — see below. That's still "English
  in the codebase" in the sense that the neutral/fallback resource and every resource *key* are English;
  the German `.de.resx` values are the only German text that belongs in this repo.
- Do not mix languages within a single commit message, PR description, or code comment.

## Localize new user-facing text

If you add or change any text the CLI prints (command/option descriptions, console output, error
messages) or any Infrastructure/Core exception or log message that reaches the user, add it to
`Strings.resx` (English) **and** `Strings.de.resx` (German) with a matching key, and reference it via
`Rettungskarten.Core.Localization.Strings.Get(...)` — never hardcode the string directly in the
consuming file. See the existing keys for naming conventions (e.g. `Command_*`, `Option_*`,
`RescueCards_*_DiscoveredCount`, `Stock_*`).

## Test with both locales

Because the app's own output is localized and depends on `CultureInfo`, a change touching
`Strings.*.resx`, `Program.cs`'s `--lang` handling, or any code that calls `Strings.Get(...)` must be
tested under both supported locales before it's considered done:

```bash
dotnet run --project src/Rettungskarten.Cli -- --lang en <command>
dotnet run --project src/Rettungskarten.Cli -- --lang de <command>
```

When writing or updating unit tests that exercise `Strings`, set `Strings.OverrideCulture` explicitly
(don't rely on the ambient OS culture, which will differ across machines and CI runners) and reset it
in `Dispose()` — see `StringsTests` for the pattern. Never assert on a literal string that is itself a
localized resource value (e.g. don't `Assert.Contains("ist keine Zahl", ...)`) — assert on the
underlying data (a model name, a count) instead, so the test doesn't silently depend on one language.

## Build, test, and review before every commit

Before creating any commit in this repository:

1. `dotnet build` — must succeed with no errors (warnings should be looked at, not ignored, before
   committing code that introduces new ones).
2. `dotnet test` — the full suite must pass. If your change touches localization, also do the
   `--lang en` / `--lang de` manual check above.
3. Run a Claude code review of the change (the `/code-review` workflow, or the `code-review` skill)
   and address what it finds before committing — either fix the finding or, if you deliberately decide
   not to, say why in the commit/PR description rather than silently ignoring it.

Do not commit if any of these three steps fail or haven't been done. This applies to every commit, not
just the first one in a session — re-run all three after further edits, even small ones.
