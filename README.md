# OnePondy

One app for Puducherry — tourism, food, rides, stays, activities, local shopping and services.

The product, architecture and phase plan are in
[OnePondy_Functional_Specification.md](OnePondy_Functional_Specification.md). Development runs on
Windows without Docker; production runs on IIS (spec §13).

## Repository layout

```text
OnePondy/
├── apps/
│   └── customer-mobile/      # React Native + Expo customer app
├── backend/                  # ASP.NET Core solution: Api, Domain, Application, Infrastructure, Worker
├── database/scripts/         # DBA-run SQL scripts (database creation, PostGIS)
├── .config/dotnet-tools.json # pinned dotnet-ef
├── .github/workflows/ci.yml  # CI: mobile checks + backend build and tests
└── package.json              # npm workspaces
```

`apps/web-console` (Phase 3) and `apps/driver-mobile` (Phase 8) are added when their phases start.

## Prerequisites

| Tool | Version used | Notes |
|---|---|---|
| Node.js | 22 (see `.nvmrc`) | npm 11 |
| .NET SDK | 10.0.403 or later 10.0.x (see `backend/global.json`) | |
| JDK | 17 | `JAVA_HOME` must point to it |
| Android Studio | with Android SDK, platform 36 and an emulator | `ANDROID_HOME` must be set |
| PostgreSQL | 18, installed as a Windows service | PostGIS is needed from Phase 2 (see below) |

iOS builds need EAS Build or a Mac (spec D4); they are not part of local setup.

## First-time setup

Run from the repository root in PowerShell.

```powershell
npm install
dotnet tool restore
```

### Database

Create the development role and database once, choosing your own password. If `psql` is not on
your `PATH`, use `& "C:\Program Files\PostgreSQL\18\bin\psql.exe"`.

```powershell
psql -U postgres -v app_password=<choose-a-password> -f database/scripts/create-dev-database.sql
```

Store the connection string in .NET user secrets (kept outside the repository):

```powershell
cd backend
dotnet user-secrets set "ConnectionStrings:OnePondy" "Host=localhost;Port=5432;Database=onepondy_dev;Username=onepondy_app;Password=<your-password>" --project src/OnePondy.Api
```

PostGIS is not needed until Migration 1 (Phases 2–4). When that phase starts, install it with
PostgreSQL Stack Builder (Spatial Extensions > PostGIS) and run
`psql -U postgres -f database/scripts/enable-postgis.sql`.

### API

```powershell
dotnet run --project backend/src/OnePondy.Api
```

The API listens on `http://localhost:5080`:

| URL | Expected |
|---|---|
| `/health/live` | `Healthy` — the process is running |
| `/health/ready` | `Healthy` when PostgreSQL is reachable, `Unhealthy` (503) otherwise |
| `/openapi/v1.json` | OpenAPI document (Development only) |

For a physical phone, run with `--urls http://0.0.0.0:5080` and allow port 5080 through Windows
Firewall.

### Customer app

Start an emulator from Android Studio (Device Manager), then:

```powershell
cd apps/customer-mobile
copy .env.example .env
npm run android   # first run: builds and installs the development build (several minutes)
npm start         # later runs: start Metro and open the installed app
```

The Android emulator reaches your PC at `10.0.2.2`; a physical phone uses your PC's LAN IP.

## Checks

```powershell
npm run format:check    # Prettier
npm run lint            # ESLint, including spec boundary rules
npm run typecheck       # TypeScript strict
npm test                # Jest + React Native Testing Library

dotnet build backend/OnePondy.sln
dotnet test backend/OnePondy.sln
```

Database integration tests are skipped unless `ONEPONDY_TEST_DATABASE` holds a connection string:

```powershell
$env:ONEPONDY_TEST_DATABASE = "Host=localhost;Port=5432;Database=onepondy_dev;Username=onepondy_app;Password=<your-password>"
dotnet test backend/OnePondy.sln
```

CI (`.github/workflows/ci.yml`) runs the same checks on every pull request.

## Troubleshooting

**`Downloading … gradle-x.y.z-bin.zip failed: timeout (10000ms)` on the first Android build.**
Gradle distributions are served from `release-assets.githubusercontent.com`, and on some networks
one of its addresses is unreachable. `curl` retries another address but Java does not. Download the
zip with `curl` into the wrapper cache folder the failed run created, then build again:

```powershell
$dir = (Get-ChildItem "$env:USERPROFILE\.gradle\wrapper\dists\gradle-9.4.1-bin" -Directory)[0].FullName
curl.exe -fL -o "$dir\gradle-9.4.1-bin.zip" https://services.gradle.org/distributions/gradle-9.4.1-bin.zip
# Compare with https://services.gradle.org/distributions/gradle-9.4.1-bin.zip.sha256
(Get-FileHash "$dir\gradle-9.4.1-bin.zip" -Algorithm SHA256).Hash
```

Use the version in `apps/customer-mobile/android/gradle/wrapper/gradle-wrapper.properties`.

## Secrets

Never commit secrets (spec §12). Local secrets live in .NET user secrets and in
`apps/customer-mobile/.env` (git-ignored). Anything in `.env` prefixed `EXPO_PUBLIC_` ships inside
the app, so it must never be secret.

## Phase status

| Phase | Status |
|---|---|
| 0 — Repository, tooling and CI | Done — see verification below |
| 1 — Design system and app shell | Next |

### Phase 0 verification (2026-10-26)

Environment: Windows 11, Node 22.23, npm 11.12, .NET SDK 10.0.403, JDK 17, Expo SDK 58
(React Native 0.88), PostgreSQL 18, Android emulator Pixel 7 (API 36).

| Acceptance criterion (spec §10, Phase 0) | Result |
|---|---|
| A fresh clone can be configured from documented steps | Setup steps above. `npm ci` from a clean state succeeded. |
| No secrets are committed | Secrets are only in user secrets and git-ignored `.env`; a scan of tracked files found none. |
| Mobile app, API and database connection verified locally | Development build: `BUILD SUCCESSFUL`, installed and rendered on the emulator. API on `http://localhost:5080`: `/health/live` and `/health/ready` both `Healthy` against a PostgreSQL 18 database created with `create-dev-database.sql`. Worker starts as a console app. |
| Lint, type-check and baseline tests pass locally and in CI | Prettier, ESLint, `tsc` pass. Jest: 1/1. Backend Release build: 0 warnings (warnings are errors). xUnit: 12 passed, plus 1 database test that passes when `ONEPONDY_TEST_DATABASE` is set. CI workflow added; it runs on the first push to GitHub. |

Known limitations:

- The database checks ran against a temporary PostgreSQL 18 cluster. Your own `onepondy_dev`
  database still needs the setup step above, because it needs your PostgreSQL admin password.
- PostGIS is not installed yet; it is needed from Phase 2–4 (see Database).
- The CI workflow has not run yet, because the repository has no GitHub remote.
- The app identifier `com.onepondy.customer` is provisional (spec D17).
