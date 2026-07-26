# Storava — Storage Intelligence & Advisor

Storava scans your drives, explains **why** disk space is used, and proposes a safe plan to
reclaim it. It **only analyzes, advises and plans** — no file or folder is ever deleted,
moved or renamed without your explicit selection and confirmation. The AI advisor can
recommend, but it can never act.

> Status: **Phase 4 — OpenRouter AI Advisor, Reporting & Privacy** (complete).
> Status: **Desktop Phase 3 complete · Storava Web 1.0 / Phase 6 complete**.

## Tech stack

.NET 10 · WPF · MVVM (CommunityToolkit.Mvvm) · Material Design in XAML · Microsoft.Extensions
Hosting/DI/Logging · Serilog · SQLite (EF Core + Microsoft.Data.Sqlite) · xUnit.

## Solution layout

```
src/
  Storava.Domain          # entities, enums, value objects, Result pattern
  Storava.Contracts       # DTOs shared across boundaries
  Storava.Application     # abstractions, settings model, use cases
  Storava.Infrastructure  # SQLite persistence, settings service (hybrid EF + raw)
  Storava.Platform        # Windows/storage APIs, protected paths, DPAPI secrets (net10.0-windows)
  Storava.Rules           # rule catalog, classification, scoring, recommendations
  Storava.AI              # OpenRouter provider, payload sanitisation, response validation
  Storava.Reporting       # report model and HTML/JSON/CSV writers
  Storava.App             # WPF UI: shell, design system, localization, pages
  Storava.Web             # browser edition (ASP.NET Core MVC + Vue islands)
tests/                    # one xUnit project per layer above
```

Projects for Migrations and Plugins are added in their respective phases.

## Storava Web

The browser edition lives independently in `src/Storava.Web` and is maintained on
`master`. Phases 1-6 provide ASP.NET Core MVC and Razor, Vue 3 page-level islands,
bilingual light/dark UI, a real browser-local Web Worker scanner, versioned IndexedDB
persistence, a virtualized explorer, local rules, Canvas treemap, history, comparison,
validated `.storava-web` export/import, a consent-gated OpenRouter advisor, and a hardened
production container.

Choose a folder at `/scan`. Chromium uses the File System Access API; other supported
browsers use a `webkitdirectory` fallback. Only file metadata and root-relative paths enter
the worker and IndexedDB. File bytes, contents, absolute personal paths, and scan trees are
never uploaded. Pause, resume, cancellation, inaccessible-entry recovery, and indeterminate
real metrics are implemented without a fabricated percentage.

AI receives only aggregate categories, risk counts, rule counts, size/age buckets, and
optional depth buckets. Its selected rule signals are mapped back to real items locally, so
the Explorer can tag and filter cleanup/archive review candidates without disclosing file
names or paths. Native Chromium scans can open or permanently delete a selected local item
only after separate browser permission and exact-name confirmation. Fallback and imported
scans remain read-only.

```bash
cd src/Storava.Web
npm ci
npm run build
cd ../..
dotnet run --project src/Storava.Web/Storava.Web.csproj
```

Open `http://localhost:5120`. Detailed architecture and visual-system decisions are in
`docs/WEB_ARCHITECTURE.md`, `docs/WEB_DESIGN_LANGUAGE.md`, and
`docs/WEB_LOCAL_DATA.md`. Production Docker, TLS, health-check, verification, and rollback
instructions are in `docs/WEB_DEPLOYMENT.md`.

```bash
docker compose up --build
```

The production container is then available at `http://localhost:8080`.

## Run

```bash
dotnet run --project src/Storava.App/Storava.App.csproj
```

Local data (SQLite + logs) lives under `%LOCALAPPDATA%\Storava`.

## Test

```bash
dotnet test Storava.slnx
```

## Implemented so far

**Phase 1 — foundation & design system**

- Professional navigation shell with grouped rail and Material Design theming.
- Live **light/dark** theme and configurable accent color.
- Live **Persian (RTL)** / **English (LTR)** switching — no restart, resource-based strings.
- Design system: palette, typography scale, spacing tokens, reusable card/nav styles.
- Onboarding, Dashboard and Settings pages; honest placeholders for later phases.
- SQLite-backed settings persistence and structured Serilog logging.

**Phase 2 — scanner, data model & persistence**

- Streaming disk scanner: iterative post-order walk that aggregates folder sizes and writes
  every item straight to SQLite, so the tree is never held in memory.
- Real **pause / resume / cancel** and live progress (files, folders, bytes, errors, elapsed).
- Continues past inaccessible paths (recorded as errors), detects **reparse points** and never
  descends into them, so junction loops are impossible.
- Exclusions by path and by extension; Quick vs Deep mode (Deep also reads on-disk size).
- Batched inserts (5,000 rows/transaction) with indexes for parent, size, type and name.
- **Scan Explorer**: lazily loaded tree, largest-items table, name search and a detail pane
  that flags protected and reparse-point items.
- Dashboard surfaces the last scan and its top findings, loaded from the database, so results
  survive an app restart.

Measured on a real run: 21,765 files / 1,941 folders / 13 GB of `C:\Windows\System32` scanned
with 19 unreadable paths skipped and no interruption.

**Phase 3 — rule engine, classification & visual analysis**

- Local rule catalog (~35 rules, no AI) covering NuGet, npm/pnpm/Yarn, Gradle, Maven, Docker,
  WSL, Hugging Face, Ollama, PyTorch, pip/Conda, Android SDK & emulators, Unity, Unreal, Steam,
  browser caches, temp files, crash dumps, VM disks and more — each with bilingual text, risk
  level, permitted actions and the *official* relocation method where one exists.
- Items are classified as the scan streams them to storage, via a sink decorator, so no second
  pass over millions of rows is needed.
- Transparent scoring (size, regeneratable, known-cache, inactivity, move benefit, minus system
  risk, active usage and migration risk) used **only** for ranking and explanation.
- Recommendations are generated locally, bound to a real scan item id, and always default to
  `NoAction`. Protected paths can never produce a recommendation.
- **Analysis page**: custom squarified treemap (drill-down, hover, tooltips, colour by category
  or risk), donut category breakdown and top consumers.
- **Recommendations page**: ranked cards with risk badge, reason, reclaimable space, confidence,
  official method and warnings.

Category bytes are attributed to the outermost classified folder, so a `node_modules` subtree
counts as package cache rather than thousands of unrecognised files. On a real developer tree
this moved identification from 4% to 99% of scanned bytes.

**Phase 4 — OpenRouter AI advisor, reporting & privacy**

- **Reports page**: builds a report from the last scan and exports it as HTML, JSON or CSV.
- **Two-step AI flow.** *Prepare* assembles the request locally and renders it verbatim; the
  Send button stays disabled until you tick the approval box. The approval is a token bound to a
  SHA-256 fingerprint of the payload you saw, so changing a setting, the language or the scan
  invalidates it — data you have not read cannot be transmitted.
- **Sanitisation before anything leaves the machine.** Real paths become placeholders
  (`<UserProfile>`, `<Drive-C>`, `<PrivateFolder-3>`), the account name is replaced, and the
  payload carries only category aggregates, rule-classified candidates and — if you leave that
  toggle on — up to 15 large *unrecognised folders*, never file names or contents. A final check
  re-scans the rendered payload for the account name and profile path; if either survived, the
  payload is *blocked* rather than sent.
- **Every reply is validated against the local scan.** Suggestions must reference a scan item
  that was actually in the payload, must not touch a protected path, and must not ask for an
  action the local rules forbid. Anything else is discarded, counted and shown on the page — the
  AI cannot invent a target, and it has no access to any delete or migration service at all
  (`Storava.AI` references only Domain, Contracts and Application).
- The API key is entered in Settings, encrypted with **Windows DPAPI** under
  `%LOCALAPPDATA%\Storava\secrets`, kept out of the database so no export can carry it, and
  never bound to an observable property or written to a log.
- Model, base URL, temperature, token cap, timeout and retry count are configurable, and
  out-of-range values are clamped instead of rejected. Retries use exponential backoff and only
  fire for transient failures (rate limit, server error, network, timeout) — never for a rejected
  key or a malformed reply.
- Typed failures (no key, unauthorized, rate limited, timeout, network, malformed, unknown
  model) each get their own bilingual message, and a long request can be cancelled.
- Sanitisation itself has no off switch, by design. The two settings that do exist —
  unrecognised-folder analysis and the narrative report — each change what is actually sent.
