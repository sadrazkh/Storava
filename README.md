# Storava — Storage Intelligence & Advisor

Storava scans your drives, explains **why** disk space is used, and proposes a safe plan to
reclaim it. It **only analyzes, advises and plans** — no file or folder is ever deleted,
moved or renamed without your explicit selection and confirmation. The AI advisor can
recommend, but it can never act.

> Status: **Desktop Phase 7 — Portable archives & resumable scans** (complete) ·
> **Storava Web / Phase 7** (complete) · **Phase 8 — companion Agent** (pairing and the
> browser↔Agent channel complete; scanning and local actions still to come).

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
  Storava.Migrations      # preflight, execution guard, step-by-step plan execution
  Storava.Reporting       # report model and HTML/JSON/CSV writers
  Storava.App             # WPF UI: shell, design system, localization, pages
  Storava.Web             # browser edition (ASP.NET Core MVC + Vue islands)
  Storava.Agent           # companion Agent: the local process the browser edition pairs with
tests/                    # one xUnit project per layer above
```

Projects for Migrations and Plugins are added in their respective phases.

## Storava Web

The browser edition lives independently in `src/Storava.Web` and is maintained on
`master`. Phases 1-7 provide ASP.NET Core MVC and Razor, Vue 3 page-level islands,
bilingual light/dark UI, a real browser-local Web Worker scanner, versioned IndexedDB
persistence, a virtualized explorer, local rules, Canvas treemap, history, comparison,
validated `.storava-web` export/import, a consent-gated OpenRouter advisor, PostgreSQL-backed
Identity accounts and sessions, and a hardened production container.

Choose a folder at `/scan`. Chromium uses the File System Access API; other supported
browsers use a `webkitdirectory` fallback. Only file metadata and root-relative paths enter
the worker and IndexedDB. File bytes, contents, absolute personal paths, and scan trees are
never uploaded. Pause, resume, cancellation, inaccessible-entry recovery, and indeterminate
real metrics are implemented without a fabricated percentage.

AI data depth is user-selectable: Essential sends totals/categories/risks, Balanced adds
rule/size/age/depth distributions, and Detailed adds anonymous category-risk and per-rule
byte evidence. None sends names, paths, extensions, or contents. Selected rule signals are
mapped back to real items locally, so
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

Set `STORAVA_DB_PASSWORD`, `STORAVA_PUBLIC_BASE_URL`, and the `STORAVA_SMTP_*` variables for
confirmation and password-reset email. The production container is then available at
`http://localhost:8080`.

## Companion Agent

A browser can only see the folder you picked, and only ever knows a path relative to it. The
Agent runs on your own machine, so it has the real file system — and it talks to the Storava page
in your browser over loopback rather than through the server, because a scan that went through the
server would be a scan that left your machine.

Pairing is implemented. Generate a code on your account page and, on the machine you want to
connect:

```bash
dotnet run --project src/Storava.Agent/Storava.Agent.csproj -- pair --server https://storava.example
```

The Agent generates a key pair locally and presents only the public half; the private key is
encrypted with Windows DPAPI under `%LOCALAPPDATA%\Storava\Agent`. Codes are stored only as a
hash, last ten minutes, and pair exactly one machine. `storava-agent status` prints the key
fingerprint to compare against the account page, and `storava-agent unpair` forgets the pairing
locally — removing the device on the account page destroys the secret that would let a browser
reach it at all.

Nothing about the machine's contents reaches the server: pairing records that an Agent exists,
what to call it, and whether it is still allowed.

Then run it, and open **Companion Agent** in the workspace at `/scan`:

```bash
dotnet run --project src/Storava.Agent/Storava.Agent.csproj -- serve
```

The Agent listens on `127.0.0.1` only, answers just the one site it is paired with, and requires a
five-minute pass the page fetches from your account. Since Chrome 142 the browser also asks your
permission the first time a public site reaches anything on your local network; the panel explains
that prompt rather than reporting a bare network error. Removing the device on your account page
destroys the secret those passes are signed with, so no new one can be issued.

Connecting proves the channel and does nothing else yet — reading drives and scanning through the
Agent are the next stage.

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

**Phase 5 — Migration Center**

The first and only page that changes your files. Everything before it produced documents.

- **Dry run first.** Every step of the saved plan is re-checked against the disk as it is *now* —
  the folder still exists, is not a junction, is not protected, and measures however much it
  measures today rather than whatever the scan recorded. Blocked steps are listed with the reason
  rather than quietly dropped, and the reclaimable figure shown is the freshly measured one.
- **One step at a time, and each one confirmed by hand.** A step runs only after you pick its
  destination and type the folder's own name. Typing that name mints an approval bound to a
  SHA-256 fingerprint of the step; changing the destination afterwards invalidates it, so an
  approval can never be spent on something other than what you read.
- **Deletion means the Recycle Bin.** `IFileSystemActions` has no permanent-delete operation at
  all — not even for cleaning up a copy Storava made itself — so no code path in the app can
  destroy data outright.
- **A move copies, verifies, and only then removes.** The copy is checked against a fresh
  measurement of the source (bytes *and* file count) before the original goes to the Recycle Bin.
  If the copy is short, the copy is discarded and the original is untouched. If the original
  cannot be recycled, the copy is discarded instead, putting the machine back where it started.
  Because the source is only ever removed after a verified copy exists, both never fail to exist.
- **Interruptions are recovered from the disk, not guessed at.** The step row is written while it
  is still marked running, so a crash mid-operation leaves a trace: on the next visit Storava
  reads which of the two paths survived and settles the step as done, undone, or failed.
- **Links back.** Where no official relocation setting exists, an NTFS junction is left at the old
  path so tools that hard-code it keep working. A junction needs no administrator rights, which is
  why it is preferred over a symbolic link. If only the link fails, the step still counts as done —
  the space was freed and the data is safe — and says so.
- Reparse points are counted but never followed, by both the measure and the copy, so a move can
  never drag in a folder you did not choose.

**Phase 6 — history, trend and comparison**

- **Compare any two scans of the same folder** and see what actually moved: folders that grew,
  shrank, appeared or disappeared, ordered by how far they moved in either direction. Comparing
  two scans of *different* roots is refused rather than producing a diff where everything looks
  new; whichever ran first is used as the baseline no matter which you picked first.
- **Nested changes are marked, not double-counted.** One cache growing by 3 GB would otherwise be
  reported for itself and again for every folder above it. Rows inside another change are hidden
  by default and labelled when shown — the same rule the storage plan applies to nested steps.
- Movement under a megabyte is left out: on a developer machine that is log churn, and listing it
  buries the findings that matter.
- **Category movement** shows which kinds of storage are responsible for the difference.
- **A trend for one root**, scaled against its largest scan so the shape is readable even when the
  scans are within a few percent of each other. Only completed scans count — a cancelled run
  stopped partway and its total would read as a cliff that never happened.
- **The record of what Storava did.** Every plan run appears with its steps, their outcome and the
  space it actually freed.
- **Pruning is honest about what it keeps.** Deleting a stored scan removes its items and its
  advice, and leaves your files alone. The execution log is deliberately kept: it records real
  changes to your disk, which outlive the scan that suggested them.

**Phase 7 — portable archives and resumable scans**

- **A scan can leave the machine it was taken on.** Any stored scan exports to a `.storava` file
  from the History page: a ZIP holding the scan, its items as streamed JSON Lines, its category
  totals and its advice, plus a manifest with a SHA-256 per entry. Exporting a scan of any size
  never holds the tree in memory, and the file is written under a temporary name so an interrupted
  export cannot leave a half-written archive under the name you chose.
- **The archive cannot carry a secret, by construction rather than by filtering.** The service
  reads only the scan tables, so there is nothing for settings or an API key to travel in — the
  manifest says so, and a test asserts it.
- **Import describes the file before it touches anything.** The manifest is read first and shown:
  which folder, when it was scanned, how many items and how much advice. An archive that was
  truncated or edited fails its hash check and nothing is imported. Re-importing replaces the
  earlier copy rather than adding a second one, and when the id belongs to a scan measured *here*,
  the confirmation says plainly that a local scan will be overwritten.
- **An imported scan is labelled as somebody else's disk.** It is fully browsable, comparable and
  reportable, but the Storage Plan and Migration Center will not fall back to it — a path from
  another machine that happens to exist here too would name a folder the scan never looked at.
- **A scan that stopped partway can be carried on.** Cancel a scan, close the app, come back: the
  History page offers to continue it. Because the walk keeps an explicit stack, what is outstanding
  is exactly the chain of folders still on it, and that is what gets stored — with the totals each
  had reached, so the subtrees already finished are never measured again.
- **Resuming does not double-count and does not skip.** The list of entries already consumed is not
  stored — it can run to hundreds of thousands of names, and the database already holds them. Each
  unfinished folder is re-enumerated and the children already written under it are skipped, which
  is one query per level of depth rather than one per folder. The tests assert the property that
  matters: a resumed scan reports the same bytes, files and folders as one uninterrupted walk of
  the same tree, and stores every item exactly once — including when the resumed run is itself
  interrupted.
- Resume state is kept only while there is genuinely something left to walk, is dropped when the
  scan completes, and is discarded rather than guessed at if it cannot be read. An imported scan
  never carries one: pending work belongs to the machine that produced it.
