# Storava Web deployment

Storava Web is one ASP.NET Core application containing its compiled Vue assets. PostgreSQL
stores accounts, sessions, future paired-device ownership, and usage-ledger entries. Browser
scan data remains in IndexedDB and is never stored in PostgreSQL. No separate frontend host,
OpenRouter proxy, scan-data volume, or PWA install is required.

## Production image

Build and run from the repository root:

```bash
docker build --pull -t storava-web:1.0.0 .
docker run --rm -p 8080:8080 --read-only \
  --tmpfs /tmp \
  --mount type=volume,source=storava-data-protection,target=/home/app/.aspnet/DataProtection-Keys \
  --cap-drop ALL \
  --security-opt no-new-privileges \
  -e Database__Provider=Postgres \
  -e 'ConnectionStrings__AccountDatabase=Host=database;Database=storava;Username=storava;Password=replace-me' \
  -e DataProtection__KeysPath=/home/app/.aspnet/DataProtection-Keys \
  storava-web:1.0.0
```

Or:

```bash
export STORAVA_DB_PASSWORD='replace-with-a-long-random-secret'
export STORAVA_PUBLIC_BASE_URL='https://storava.example'
docker compose up --build
```

Check `http://localhost:8080/health`, then open `http://localhost:8080/`. The image runs
as the .NET non-root application user, has a Docker health check, contains no source maps,
and does not contain Node.js or the .NET SDK in its runtime layer.

## TLS and reverse proxy

Folder access and service workers require a secure context outside localhost. Terminate
TLS at the ingress/reverse proxy and forward traffic to container port `8080`. The container
sets `WebSecurity__UseHttpsRedirection=false` because the ingress owns the HTTP-to-HTTPS
redirect. If ASP.NET Core itself terminates TLS instead, remove that variable and configure
the HTTPS endpoint and certificate normally.

Forward only the public origins you intend to serve. Keep `AllowedHosts` restricted in a
host-specific configuration when the final domain is known. Do not add upload routes,
multipart limits, scan storage volumes, or an unrestricted AI proxy: scans and file
operations are browser-local.

## Runtime configuration

Supported non-secret settings:

| Variable | Default | Purpose |
| --- | ---: | --- |
| `WebSecurity__RateLimitPermit` | `120` | Requests allowed per IP/window |
| `WebSecurity__RateLimitWindowSeconds` | `60` | Fixed-window duration |
| `WebSecurity__UseHttpsRedirection` | `true` | ASP.NET-owned HTTPS redirect |
| `Database__Provider` | `Postgres` | Production account database provider |
| `Database__ApplyMigrations` | `true` | Apply checked-in account migrations at startup |
| `DataProtection__KeysPath` | empty | Persistent cookie/token key-ring directory |
| `AccountEmail__DeliveryMode` | `Smtp` | Production confirmation/reset delivery |
| `AccountEmail__PublicBaseUrl` | empty | Canonical public HTTPS origin used in security emails |
| `AccountEmail__Host` / `Port` / `UseSsl` | empty / `587` / `true` | SMTP endpoint |
| `AccountEmail__FromAddress` / `FromName` | empty / `Storava` | Sender identity |

OpenRouter API keys are entered by each user and remain only in page memory. Never inject
an API key into the image, JavaScript bundle, environment, logs, or ASP.NET configuration.
Treat the PostgreSQL password, SMTP password, and Data Protection key volume as secrets.
Persist the key volume across deployments; replacing it signs users out and invalidates
outstanding account tokens.

## Release verification

Run these before publishing an image:

```bash
dotnet restore Storava.slnx
dotnet build Storava.slnx --no-restore
dotnet test Storava.slnx --no-build
cd src/Storava.Web
npm ci
npm audit --audit-level=high
npm run lint
npm run typecheck
npm run test
npm run build
npm run test:e2e
cd ../..
docker build --pull -t storava-web:1.0.0 .
```

Review `/`, `/scan`, `/privacy`, `/account/register`, `/account/login`, `/account`, and
`/health` through the production proxy. Verify confirmation and reset delivery against the
configured SMTP service. Verify English
and Persian, LTR and RTL, light and dark themes, fallback folder selection, AI consent,
AI-target filtering, and the explicit delete confirmation. A real deletion test must use a
disposable folder.

## Browser action boundary

- Native Chromium folder selection can retain a local directory handle in IndexedDB.
- Opening a file requests read access when needed.
- Deletion requests separate `readwrite` permission and exact-name confirmation.
- Folders are deleted recursively only after that confirmation.
- Fallback and imported scans are always read-only.
- Browsers expose a root label and relative path, not an operating-system absolute path or
  a universal “reveal in Finder/Explorer” API. Storava therefore offers local navigation,
  a copyable root-relative address, and direct browser-authorized actions.
- AI sees only the exact aggregate preview approved by the user. Essential, Balanced, and
  Detailed profiles change aggregate depth but never include names, paths, extensions, or
  contents. The browser maps selected signal classes to real local items after the response.

## Rollback

Application images are replaceable, while PostgreSQL and the Data Protection key ring are
stateful. Back them up before a migration. Roll back the application only to a version
compatible with the current account schema; database down-migrations require an explicit,
tested recovery plan. Scan history, directory handles, and AI reports live in each user's
browser and are not changed by a server rollback.
