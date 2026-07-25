# Storava Web — Phase 1 Architecture Audit

## Isolation decision

- The original checkout remains on `master` and was clean before work began.
- Web development runs in the adjacent `Storava-Web` worktree on
  `feature/storava-web`.
- `src/Storava.Web` is an independent executable. It does not reference the WPF,
  Windows platform, or desktop persistence projects.
- The desktop projects remain buildable from the same solution and no existing C# or XAML
  implementation was changed.

## Existing solution audit

| Project | Current responsibility | Web reuse decision |
| --- | --- | --- |
| `Storava.Domain` | Entities, enums, value objects, results | Conceptually reusable; no Phase 1 dependency |
| `Storava.Contracts` | Boundary DTO placeholder | Intended shared contract home; currently empty |
| `Storava.Application` | Scanner abstractions, coordinator, settings contracts | Mixed: lifecycle ideas reusable, path-based scanner contracts are desktop-shaped |
| `Storava.Rules` | Deterministic catalog, matching, scoring, recommendations | Logic is mostly portable; matching currently assumes Windows separators and `ScanItem.Path` |
| `Storava.Infrastructure` | SQLite persistence and settings | Desktop-only implementation; Web will use IndexedDB |
| `Storava.Platform` | Disk traversal, drive discovery, protected Windows paths | Desktop-only |
| `Storava.App` | WPF shell, views, theme, localization, native picker | Desktop-only |

### Safely shareable concepts

- Category, risk, action, scan-mode, and item-type definitions.
- Recommendation/report contracts after personal paths are removed.
- Versioned import/export schemas.
- Rule identifiers, localized descriptions, risk levels, and action capability flags.
- Scoring inputs and deterministic score rules after replacing desktop entity coupling.
- Localization keys and test fixtures.

### Desktop-only implementation

- WPF controls, XAML resources, converters, view models, dialogs, and navigation.
- `System.Windows`, `Microsoft.Win32`, Material Design WPF, and Windows theme APIs.
- `DiskScanner`, `DriveInfo` enumeration, Win32 allocated-size calls, reparse-point logic,
  protected system paths, junction handling, administrator checks, and file locking.
- SQLite repositories and desktop settings storage.
- Full absolute path semantics and Windows path normalization.

## Conflict and coupling risks

1. `ScanItem` exposes `System.IO.FileAttributes` and a full `Path`; exporting it directly to
   the browser would retain desktop and privacy assumptions.
2. `RuleEngine` normalizes patterns to backslashes, so directly compiling the current rules
   into TypeScript would mis-handle browser-relative `/` paths.
3. `ScanRequest` requires `RootPath`; browser access is capability-handle based and cannot be
   represented by a server filesystem path.
4. `Storava.Infrastructure` stores full paths in SQLite; Web persistence must use IndexedDB
   with sanitized, root-relative paths and batched records.
5. WPF localization resources are XAML dictionaries. Sharing their presentation mechanism
   would couple Web to WPF; only stable keys and translated values should converge.
6. Desktop scanning can inspect platform metadata and allocated size. Web must clearly label
   unavailable metadata and must not promise drive-wide access where the browser cannot grant it.

## Chosen Web boundary

```text
ASP.NET Core MVC + Razor
        │ serves localized document shell, assets, security policy
        ▼
Vue page-level island (no global SPA/router)
        │
        ├── preference/localization resources
        ├── browser capability adapter
        ├── native directory picker / webkitdirectory adapter
        ├── service-worker shell
        └── Phase 2 boundary
              ├── Web Worker scanner
              ├── IndexedDB repositories
              └── Web rule adapter
```

The backend does not accept file bytes, file contents, multipart scan input, full file trees,
or personal absolute paths. The integration suite inspects the public MVC endpoint metadata
and fails if an `IFormFile` or upload route is introduced.

## Phase 2 migration seams

- Introduce browser-native scan contracts under `ClientApp/models`; do not reuse
  `ScanRequest.RootPath`.
- Store only root-relative paths, with the root label separated from item records.
- Extract a versioned JSON rule schema and shared fixtures before sharing catalog data.
- Implement iterative traversal and batching inside a worker; send throttled aggregate
  messages to the Vue island.
- Persist sessions/items in versioned IndexedDB stores; never place a large scan in one
  record.
- Treat File System Access handles as local browser objects. Never serialize or transmit
  them.

## Phase 1 server surface

- `GET /` — product landing and onboarding island.
- `GET /privacy` — privacy architecture island.
- `GET /health` — health check.
- `GET /Home/Error` — localized production error surface.
- Static assets and PWA resources.

There is no scan API, upload API, proxy, AI endpoint, account system, or telemetry pipeline.
