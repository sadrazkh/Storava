# Storava — Storage Intelligence & Advisor

Storava scans your drives, explains **why** disk space is used, and proposes a safe plan to
reclaim it. It **only analyzes, advises and plans** — no file or folder is ever deleted,
moved or renamed without your explicit selection and confirmation. The AI advisor can
recommend, but it can never act.

> Status: **Phase 1 — Foundation, Architecture & Design System** (complete).

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

## Phase 1 features

- Professional navigation shell with grouped rail and Material Design theming.
- Live **light/dark** theme and configurable accent color.
- Live **Persian (RTL)** / **English (LTR)** switching — no restart, resource-based strings.
- Design system: palette, typography scale, spacing tokens, reusable card/nav styles.
- Onboarding, Dashboard (real drive data) and Settings pages; honest placeholders for
  pages scheduled in later phases.
- SQLite-backed settings persistence and structured Serilog logging.
