# Storava — Storage Intelligence & Advisor

Storava scans your drives, explains **why** disk space is used, and proposes a safe plan to
reclaim it. It **only analyzes, advises and plans** — no file or folder is ever deleted,
moved or renamed without your explicit selection and confirmation. The AI advisor can
recommend, but it can never act.

> Status: **Phase 2 — Scanner, Data Model & Persistence** (complete).

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
