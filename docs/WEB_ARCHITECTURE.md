# Storava Web — Phase 1 Architecture Audit

## Isolation decision

- The Web work was isolated in a dedicated worktree through Phase 5 and was then merged.
- By explicit project-owner decision, continued Web development now runs directly on
  `master`; the Web executable remains isolated by project and dependency boundaries.
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

## Phases 2-4 implementation

The earlier migration seams are now implemented without changing Desktop projects:

- `GET /scan` mounts a dedicated Vue island for scanning, exploration, history, and transfer.
- File System Access handles or fallback `File` objects cross only into a module Worker.
- Iterative traversal yields between bounded 200-item batches and supports pause, resume,
  cancellation, and per-entry access-error recovery.
- Sessions and metadata items use separate versioned IndexedDB records and compound indexes.
- Browser-specific TypeScript rules annotate local records; the Desktop rule engine remains
  untouched until a shared versioned schema can be introduced safely.
- Explorer rendering virtualizes a bounded result window. Treemap rendering uses one Canvas
  rather than a DOM node per item.
- `.storava-web` transfer is versioned NDJSON with relative paths, streamed validation,
  batched writes, and an integrity trailer.

The ASP.NET server surface remains page delivery, static assets, health, localization, and
security middleware. No scanned metadata or file upload endpoint was introduced.

## Phases 5-6 implementation

- OpenRouter is direct browser BYOK. The key remains in component memory and only official
  OpenRouter API hosts are accepted.
- The exact aggregate payload is shown before consent. Names, paths, extensions, contents,
  keys, and tokens are excluded and covered by unit and browser tests.
- Structured AI output can reference only a closed list of local rule signal identifiers.
  It cannot identify a file. After validation, the browser maps those signals to local
  records, persists the result in IndexedDB, and exposes AI-target filtering in Explorer.
- Native directory handles are stored as browser-local structured-clone objects. A handle
  is never exported, logged, serialized to JSON, or transmitted.
- Opening a real file requests read permission. Deletion requests `readwrite` permission,
  displays the root-relative address, requires exact-name confirmation, performs the local
  File System Access operation, and only then updates IndexedDB aggregates.
- Fallback and imported scans are read-only because they have no reusable write-capability
  handle. Operating-system absolute paths and universal reveal-in-file-manager actions are
  intentionally not claimed.
- Production assets are built during ordinary .NET builds and publication. Source maps are
  excluded from production bundles.
- The release has a multi-stage, non-root Docker image, health check, read-only Compose
  profile, master-branch CI, browser tests, performance harness, security tests, and
  deployment/rollback documentation.

## Phase 7 implementation

- ASP.NET Core Identity owns registered users, password hashing, confirmed-email login,
  lockout, reset tokens, security stamps, and hardened application cookies.
- PostgreSQL is the production account store. SQLite is limited to local development and
  integration tests; PostgreSQL uses the checked-in EF migration.
- Every login creates a revocable account-session record. Only a normalized browser/platform
  label is retained; raw user-agent strings and IP addresses are not stored.
- Device and usage-ledger tables establish the ownership and accounting boundary for the
  companion Agent without pretending that an Agent already exists.
- Browser scan records, directory handles, and advisor results remain in IndexedDB. Creating
  an account does not upload, synchronize, or attach them to the server-side identity.
- Advisor payload depth is explicit (`essential`, `balanced`, or `detailed`). Even the detailed
  profile contains only aggregate matrices and rule evidence—never names, extensions, paths,
  file bytes, contents, or capability handles.
- Copied browser addresses are normalized and de-duplicate legacy root segments. The UI calls
  them browser-relative and explains that only the Phase 8 companion Agent can provide an
  operating-system absolute path.
