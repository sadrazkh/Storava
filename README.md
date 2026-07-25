# Storava — Storage Intelligence & Advisor

Storava scans your drives, explains **why** disk space is used, and proposes a safe plan to
reclaim it. It **only analyzes, advises and plans** — no file or folder is ever deleted,
moved or renamed without your explicit selection and confirmation. The AI advisor can
recommend, but it can never act.

> Status: **Phase 3 — Rule Engine, Classification & Visual Analysis** (complete).

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
  Storava.Platform        # Windows/storage APIs, protected paths (net10.0-windows)
  Storava.App             # WPF UI: shell, design system, localization, pages
tests/
  Storava.Domain.Tests
  Storava.Infrastructure.Tests
```

Projects for AI, Migrations, Reporting and Plugins are added in their respective phases.

## Storava Web

The browser edition is developed independently in `src/Storava.Web` on
`feature/storava-web`. Phase 1 provides the production foundation: ASP.NET Core MVC and
Razor, Vue 3 page-level islands, TypeScript/Vite, live Persian/English and light/dark
preferences, real browser capability detection, native folder-permission onboarding, a PWA
shell, security headers, health checks, rate limiting, and automated tests.

No scanner is simulated in Phase 1. Selecting a folder verifies browser permission and stops
at the explicit phase boundary. No scanned file upload API exists.

```bash
cd src/Storava.Web
npm ci
npm run build
cd ../..
dotnet run --project src/Storava.Web/Storava.Web.csproj
```

Open `http://localhost:5120`. Detailed architecture and visual-system decisions are in
`docs/WEB_ARCHITECTURE.md` and `docs/WEB_DESIGN_LANGUAGE.md`.

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
