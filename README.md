# OnePondy

One app for Puducherry — tourism, food, rides, stays, activities, local shopping and services.

The product, architecture and phase plan are in
[OnePondy_Functional_Specification.md](OnePondy_Functional_Specification.md). Development runs on
Windows without Docker; production runs on IIS (spec §13).

## Repository layout

```text
OnePondy/
├── apps/
│   ├── customer-mobile/      # React Native + Expo customer app
│   └── web-console/          # Next.js staff console: content, configuration, staff, audit log
├── backend/                  # ASP.NET Core solution: Api, Domain, Application, Infrastructure, Worker
├── database/scripts/         # DBA-run SQL scripts (database creation, PostGIS)
├── docs/                     # Policies code comments point to (retention, ...)
├── .config/dotnet-tools.json # pinned dotnet-ef
├── .github/workflows/ci.yml  # CI: mobile checks + backend build and tests
└── package.json              # npm workspaces
```

`apps/driver-mobile` (Phase 8) is added when that phase starts.

## Prerequisites

| Tool | Version used | Notes |
|---|---|---|
| Node.js | 22 (see `.nvmrc`) | npm 11 |
| .NET SDK | 10.0.403 or later 10.0.x (see `backend/global.json`) | |
| JDK | 17 | `JAVA_HOME` must point to it |
| Android Studio | with Android SDK, platform 36 and an emulator | `ANDROID_HOME` must be set; native builds need CMake 3.31.6 on Windows (see Troubleshooting) |
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

#### Sign-in secrets (Phase 2)

The API refuses to start without two signing keys. Generate each once and keep them in user
secrets, never in the repository:

```powershell
cd backend
$key = { [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) }
dotnet user-secrets set "Auth:Jwt:SigningKey" (& $key) --project src/OnePondy.Api
dotnet user-secrets set "Auth:HashingKey" (& $key) --project src/OnePondy.Api
```

Create the tables with `dotnet tool run dotnet-ef database update --project src/OnePondy.Infrastructure --startup-project src/OnePondy.Api`.

To send real SMS codes during development, use Twilio (the production SMS provider is MSG91, which
needs DLT registration first). Set the credentials as user secrets, never in a file that is
committed, then set `Auth:Otp:Delivery` to `Twilio`:

```powershell
cd backend
dotnet user-secrets set "Twilio:AccountSid" "<account sid>" --project src/OnePondy.Api
dotnet user-secrets set "Twilio:AuthToken" "<auth token>" --project src/OnePondy.Api
dotnet user-secrets set "Twilio:From" "<your Twilio number, e.g. +1555...>" --project src/OnePondy.Api
dotnet user-secrets set "Auth:Otp:Delivery" "Twilio" --project src/OnePondy.Api
```

A Twilio trial account can only text numbers verified in the Twilio console, and sending to India
may need extra permission under Messaging > Settings > Geo permissions. Only SMS is sent; email
codes still fail until an email provider is chosen. Instead of `Twilio:From` you can set
`Twilio:MessagingServiceSid`.

**Recommended for India: Twilio Verify.** It needs no phone number of your own, because Twilio
creates, sends and checks the code and routes SMS to India itself. OnePondy still applies its own
5-minute expiry, attempt limit and send limits. Create a Verify service in the Twilio console, then:

```powershell
dotnet user-secrets set "Twilio:AccountSid" "<account sid>" --project src/OnePondy.Api
dotnet user-secrets set "Twilio:AuthToken" "<auth token>" --project src/OnePondy.Api
dotnet user-secrets set "Twilio:VerifyServiceSid" "<VA... service sid>" --project src/OnePondy.Api
dotnet user-secrets set "Auth:Otp:Delivery" "TwilioVerify" --project src/OnePondy.Api
```

Verify cannot send the "account deleted" confirmation text, so that message is skipped in this
mode. On a trial account the code can only go to numbers verified in your Twilio account.

Until an SMS and email provider is chosen (spec D19), the API cannot deliver one-time codes. In
Development the `appsettings.Development.json` setting `Auth:Otp:Delivery = Log` prints each code
to the API console as a `DEVELOPMENT ONLY` warning. The API refuses that setting in any other
environment except `Testing`. The Worker (`backend/src/OnePondy.Worker`) needs the same
connection string and `Auth:HashingKey` in its own user secrets; it purges expired sign-in data
every hour (`docs/data-retention.md`).

#### Online payment (Razorpay, sandbox only)

Payments are off until you give the API Razorpay **test-mode** keys and an administrator switches the
`payments` module to "available" in the console. Set the keys as user secrets (never in a committed
file, and never paste them into chat or an issue):

```powershell
cd backend
dotnet user-secrets set "Payments:Razorpay:KeyId" "<rzp_test_... key id>" --project src/OnePondy.Api
dotnet user-secrets set "Payments:Razorpay:KeySecret" "<key secret>" --project src/OnePondy.Api
dotnet user-secrets set "Payments:Razorpay:WebhookSecret" "<webhook secret>" --project src/OnePondy.Api
```

Key id and key secret must be set together, or the API refuses to start. The API also refuses to start
with a live key (`rzp_live_...`) unless `Payments:Mode` is `Live` and `Payments:AllowLive` is `true`;
leave both alone until legal review and a settlement test are complete (docs/phase-7-plan.md). The
webhook secret is used from step 7.3. Other settings, all optional: `Payments:PaymentWindow` (default
30 minutes after a partner accepts), `Payments:GatewayCheckGrace` (default 15 minutes: how long the Worker
keeps an unpaid booking while Razorpay cannot be asked whether it was paid), and
`Payments:CommissionPercent` (provisional 10: the platform's share booked to the ledger when a payment arrives, for sandbox
testing only), and `Payments:RefundsEnabled` (default `false`: while it is off, a booking that has been paid for cannot be
cancelled, so money is never kept silently; turn it on only after the cancellation policy is approved. Refunds of late or
duplicate payments are not held back by it).

**Webhook (so a payment can confirm a booking).** Razorpay reports every payment result to
`POST /api/v1/webhooks/payments`; the app's own word is never trusted. In the Razorpay Dashboard
(Test Mode): Settings, Webhooks, Add New Webhook. Use the public HTTPS address of the API
(`https://<your-host>/api/v1/webhooks/payments`; Razorpay cannot reach `localhost`, so a local run
needs a tunnel), choose your own secret, and tick `payment.captured`, `order.paid`, `payment.failed`,
`refund.processed` and `refund.failed`. Then store the same secret as `Payments:Razorpay:WebhookSecret`.
Without it the endpoint answers 503 and no payment can confirm a booking. Deliveries are checked with
HMAC-SHA256 against that secret, recorded once by Razorpay's event id (a redelivery changes nothing),
and stored with only ids, amounts and statuses: no email, phone, card or UPI details.

To run the one test that calls Razorpay's real test API (it creates a ₹1 test order and charges
nothing), set `ONEPONDY_RAZORPAY_TEST_KEY_ID` and `ONEPONDY_RAZORPAY_TEST_KEY_SECRET` to a test key
pair before `dotnet test`; without them it is skipped.

**Push notifications (Expo).** Booking changes are queued as notifications in the same save as the change; the Worker sends them
(see "7.7" in docs/phase-7-plan.md). Nothing is sent until you set `Notifications:Enabled` to `true` on the Worker; until then
messages only queue (and any older than 24 hours are dropped when sending starts). It needs an Expo project; if Expo's enhanced push
security is on for it, set the access token as a secret, never in a committed file:

```powershell
dotnet user-secrets set "Notifications:Enabled" "true" --project src/OnePondy.Worker
dotnet user-secrets set "Notifications:Expo:AccessToken" "<expo access token>" --project src/OnePondy.Worker
```

The phone registers its Expo push token with `POST /api/v1/devices`; it stops receiving notifications the moment its session ends.
Notification text is generic by design and carries only the booking id. To try Expo's real service with a made-up token (nothing is
delivered to anyone), set `ONEPONDY_EXPO_LIVE_CHECK=1` before `dotnet test`.

**In the app.** The booking screen shows a Pay card (Razorpay Checkout, test mode) while a booking waits for payment, then the
receipt and any refund. The app never decides a payment succeeded: it asks `POST /api/v1/bookings/{id}/payment/refresh`, which
asks Razorpay itself, so this works on a laptop even though Razorpay cannot reach your webhook. Razorpay Checkout and
notifications are native modules: after pulling this change run `npx expo run:android` once to rebuild the development build.
Push tokens also need an Expo (EAS) project id in the app config (see "7.8" in docs/phase-7-plan.md); without one the app
simply does not register for notifications. In Razorpay test mode, pay with **Netbanking** and press Success on the demo bank page: it works for any account. The
usual `4111 1111 1111 1111` test card is refused as an "international card" by an Indian Razorpay account; use one of
Razorpay's Indian test cards from its documentation instead.

**Statements and payouts.** In the console, an administrator drafts a statement for a partner and period (Statements), checks it,
approves it, transfers the money themselves, then records the bank reference, which books the payout in the ledger. Partners see
approved and paid statements under their business, and can download them as CSV. Payments, refunds and reconciliation
differences are under Payments. See "7.9" in docs/phase-7-plan.md. The new migration `AddSettlementRowVersion` must be applied
like the others.

**Reconciliation.** The Worker also checks every five minutes, and once a day, that our payment records agree with
Razorpay's (see "7.6" in docs/phase-7-plan.md). A payment or refund whose webhook was lost is applied from Razorpay's own record
and noted; anything else that differs is listed for an administrator at
`GET /api/v1/admin/payments/exceptions` and closed with
`POST /api/v1/admin/payments/exceptions/{id}/resolve` and a note. Keep the Worker running, and alert on its log line
"payment reconciliation problems are waiting for an administrator".

On a server running under IIS the API does not read user secrets: put the same keys in the server's
`appsettings.<Environment>.json` and keep that file private. Apply database migrations as the
`onepondy_app` role, not `postgres`, so the tables are owned by the role the API connects as.

#### Hosting the API on IIS

1. Install the ASP.NET Core 10 Hosting Bundle and create a site pointing at the publish folder.
2. Publish: `dotnet publish backend/src/OnePondy.Api -c Release -o "<folder>"`. For a shared test
   server add `-p:EnvironmentName=Testing` so one-time codes are written to the log. IIS shows them in
   Event Viewer > Windows Logs > Application, source ".NET Runtime" (the stdout file stays empty
   until the process exits).
3. Put `appsettings.Production.json` (or `appsettings.Testing.json` for a test server) in the
   publish folder with `ConnectionStrings:OnePondy`, `Auth:Jwt:SigningKey` and `Auth:HashingKey`.
   Keep it out of source control and restrict who can read the folder.
4. Create the database with `create-dev-database.sql`, then apply the migrations as the app role
   (`dotnet ef migrations script --idempotent --output migrate.sql`, then `psql -f migrate.sql`).
   EF writes a UTF-8 byte-order mark that psql rejects (`syntax error at or near "ï»¿CREATE"`);
   remove the first three bytes before running it.
5. Check `/health/ready`. It returns `Healthy` only when the database is reachable.

Point the app at the site with `EXPO_PUBLIC_API_BASE_URL` in `apps/customer-mobile/.env`. Plain
HTTP is fine for a development network only; real users need HTTPS.

### Customer app

Start an emulator from Android Studio (Device Manager), then:

```powershell
cd apps/customer-mobile
copy .env.example .env
npm run android   # first run: builds and installs the development build (several minutes)
npm start         # later runs: start Metro and open the installed app
```

The Android emulator reaches your PC at `10.0.2.2`; a physical phone uses your PC's LAN IP.

### Web console (Phase 3)

Staff sign in with a one-time code by email, the same way customers sign in by email (spec D1,
D20). Give yourself access before anything else exists to grant it: add your email to
`Console:BootstrapAdministrators` and it becomes an administrator the first time it requests a
code, but only while no administrator exists yet.

```powershell
cd backend
dotnet user-secrets set "Console:BootstrapAdministrators:0" "you@example.com" --project src/OnePondy.Api
```

Then run the console itself:

```powershell
cd apps/web-console
copy .env.example .env.local
npm run dev
```

It listens on `http://localhost:3100` and calls the API at `ONEPONDY_API_URL` (`.env.local`,
default `http://localhost:5080`). Sign-in codes appear wherever the API logs them (console window
in Development, Event Viewer on IIS; see Sign-in secrets above). Once you hold a staff role, add
more staff from Staff → Add someone; a new person signs in the same way, with their own code.

Uploaded place photos are written to `Media:RootPath` (defaults to `App_Data/media` under the
API's folder) and served back through `/media/...` on the API, which the console proxies at its
own `/media/...` path so the browser never talks to the API directly (spec D8: a local folder for
now, behind `IMediaStorage`, so S3-compatible storage with a CDN can replace it later without
touching the console or app).

#### Hosting the console on IIS

Run the console as a Node.js Windows Service behind IIS URL Rewrite and Application Request
Routing (spec §13); IIS does not run Node.js code directly.

1. Build it: `npm run build --workspace apps/web-console`. With `output: 'standalone'` this
   produces `apps/web-console/.next/standalone`, a self-contained server that does not need
   `node_modules` installed on the target machine.
2. Copy `public` and `.next/static` into the standalone folder (Next.js does not do this for you):
   `.next/standalone/public` and `.next/standalone/.next/static`.
3. Set `ONEPONDY_API_URL` and `CONSOLE_SECURE_COOKIES=true` (the default once `NODE_ENV=production`
   is set) as environment variables for the service, not in a committed file.
4. Register `node .next/standalone/server.js` as a Windows Service (for example with NSSM) listening
   on a local port, and point an IIS site with URL Rewrite/ARR at that port. Enable HTTPS on the IIS
   side; the console's own cookies are marked `Secure` and will not be set over plain HTTP in
   production.

### Partner area and the Worker (Phase 6)

Partners are ordinary customers who own a business: they sign in at `/partner/sign-in` on the
console with an email code (no separate account type, spec §1), apply at `/partner/apply`, and an
administrator approves them under Partners. An approved business adds listings (an administrator
publishes them under Listings), availability and a booking inbox. Console administrators therefore
see two new sections, Partners and Listings.

- Uploaded GSTIN and licence documents go to `BusinessDocuments:RootPath` (default
  `App_Data/business-documents` under the API's folder), never to a folder IIS serves. Give the API's
  application pool write access to it and back it up with the database.
- **Run the Worker** (`dotnet run --project backend/src/OnePondy.Worker`, or install it as a Windows
  Service). Besides the hourly sign-in clean-up it expires booking holds every minute; without it a
  booking nobody acts on keeps its places held for ever. It needs the same connection string and
  `Auth:HashingKey` as the API.
- The customer app shows Stays and Activities once an administrator sets those two module flags to
  available under Configuration.

## Documentation

- [Backend architecture](docs/backend-architecture.md): layers, request pipeline, security, concurrency, Worker, configuration, endpoint catalogue.
- [Database architecture](docs/database-architecture.md): every table, constraint, index and trigger, with an ER diagram and the migration history.
- [Workflows](docs/workflows.md): sign-in, content verification, partner onboarding, listings, the booking lifecycle and hold expiry, step by step.
- [Phase 7 plan](docs/phase-7-plan.md): the payments, ledger and notifications plan and the decisions it depends on.
- [Data retention](docs/data-retention.md): how long authentication data and deleted-account data are kept.

## Checks

```powershell
npm run format:check    # Prettier, repository-wide
npm run lint            # ESLint for every workspace (customer-mobile, web-console)
npm run typecheck       # TypeScript strict for every workspace
npm test                # Jest (customer-mobile) and Vitest (web-console) for every workspace

dotnet build backend/OnePondy.sln
dotnet test backend/OnePondy.sln
```

The root `lint`, `typecheck` and `test` scripts run across every `apps/*` workspace
(`--workspaces --if-present`); use `--workspace apps/web-console` to target one.

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

**`ninja: error: mkdir(...): No such file or directory` while building native code.**
Generated C++ paths exceed Windows' 260-character limit, and the ninja that ships with the Android
SDK's default CMake (3.22.1, ninja 1.10) cannot handle longer paths. Enable long paths once (admin
PowerShell), install a newer CMake and point the project at it:

```powershell
# Once per machine (admin), if not already enabled:
New-ItemProperty -Path HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem -Name LongPathsEnabled -Value 1 -PropertyType DWORD -Force

# CMake 3.31.6 bundles ninja 1.12.1, which supports long paths:
& "$env:LOCALAPPDATA\Android\Sdk\cmdline-tools\latest\bin\sdkmanager.bat" "cmake;3.31.6"
```

Then add this line to `apps/customer-mobile/android/local.properties` (machine-specific; recreate
it after `npx expo prebuild --clean`), with your user name:

```properties
cmake.dir=C\:/Users/<you>/AppData/Local/Android/Sdk/cmake/3.31.6
```

## Secrets

Never commit secrets (spec §12). Local secrets live in .NET user secrets and in
`apps/customer-mobile/.env` (git-ignored). Anything in `.env` prefixed `EXPO_PUBLIC_` ships inside
the app, so it must never be secret.

## Phase status

| Phase | Status |
|---|---|
| 0 — Repository, tooling and CI | Done — see verification below |
| 1 — Design system and app shell | Done — see verification below |
| 2 — App state, authentication and accounts | Done — see verification below |
| 3 — Web console and content management | Done — see verification below |
| 4 — Tourism discovery and itineraries | Done — places, saved places, itineraries, nearby sorting (decision D6: list only, no map SDK) |
| 5 — AI trip planner | Done — behind `IItineraryPlanner`, Gemini implementation (decision D5, owner to confirm) |
| 6 — Partner listings and booking engine | Done; payment was added in Phase 7 — see verification below |
| 7 — Payments, ledger and notifications | **Built in sandbox (steps 7.1–7.9); live payments deliberately off** until the legal and tax review, D21/D22 sign-off and an end-to-end settlement check — see "Phase 7 verification" and `docs/phase-7-plan.md` |

### Phase 0 verification (2026-10-26)

Environment: Windows 11, Node 22.23, npm 11.12, .NET SDK 10.0.403, JDK 17, Expo SDK 58
(React Native 0.88), PostgreSQL 18, Android emulator Pixel 7 (API 36).

| Acceptance criterion (spec §10, Phase 0) | Result |
|---|---|
| A fresh clone can be configured from documented steps | Setup steps above. `npm ci` from a clean state succeeded. |
| No secrets are committed | Secrets are only in user secrets and git-ignored `.env`; a scan of tracked files found none. |
| Mobile app, API and database connection verified locally | Development build: `BUILD SUCCESSFUL`, installed and rendered on the emulator. API on `http://localhost:5080`: `/health/live` and `/health/ready` both `Healthy` against a PostgreSQL 18 database created with `create-dev-database.sql`. Worker starts as a console app. |
| Lint, type-check and baseline tests pass locally and in CI | Prettier, ESLint, `tsc` pass. Jest: 1/1. Backend Release build: 0 warnings (warnings are errors). xUnit: 12 passed, plus 1 database test that passes when `ONEPONDY_TEST_DATABASE` is set. CI workflow added; it runs on pull requests and on pushes to `main`. |

Known limitations:

- The database checks ran against a temporary PostgreSQL 18 cluster. Your own `onepondy_dev`
  database still needs the setup step above, because it needs your PostgreSQL admin password.
- PostGIS is not installed yet; it is needed from Phase 2–4 (see Database).
- CI has since run on GitHub: both jobs (mobile on Ubuntu, backend on Windows) passed.
- The app identifier `com.onepondy.customer` is provisional (spec D17).

### Phase 1 verification (2026-10-26)

| Acceptance criterion (spec §10, Phase 1) | Result |
|---|---|
| Core screens use design-system components rather than duplicated styles | All screens are built from `src/design-system` atoms, molecules, organisms and templates; colours, type, spacing and radii come only from tokens. |
| No user-facing string is hard-coded outside the locale files | Enforced by ESLint `i18next/no-literal-string` (verified to catch planted violations); translation keys are type-checked. |
| Layout works on small and large phones | Checked on the emulator at 1080×2400 (411×914 dp), 720×1280 (360×640 dp), 200% system font, and both combined. Large-font problems found during the check (overlapping badges, truncated tab labels and values) were fixed and re-verified. |
| Touch targets, contrast, loading, error and empty states are verified | 48 dp minimum targets (tested); 54 automated WCAG AA contrast checks across light and dark tokens; loading, empty, error and offline states are component-tested and shown in the component gallery. |
| UI looks coherent with clearly labelled sample content | Reviewed on the emulator in light, dark and system-following modes. Sample places and collections show a "Sample" badge and exist only in development builds. |

Checks: Prettier, ESLint (0 problems), `tsc` (0 errors), Jest 117/117 across 12 suites, including
end-to-end app flows: onboarding, tabs, "Coming soon", search, area picker, appearance and language.

Known limitations:

- Tamil and French are listed as "Translation in progress" until reviewed translations exist (D10).
- The brand mark, app icon, splash artwork and typeface are placeholders (D18).
- The selected area is not persisted between app launches; persistent location arrives with
  location permissions in Phase 4.
- iOS has not been built or run (D4).

### Phase 2 verification (2026-10-26)

| Acceptance criterion (spec §10, Phase 2) | Result |
|---|---|
| Reducers are pure and tested; no API or storage side effects inside reducers | Auth reducer tests cover restore, sign-in, name change and sign-out, including no-op cases. ESLint forbids API, storage and navigation imports under `src/state`. |
| Guests can use public screens; protected screens and actions require a valid session | App-flow tests: guests see sign-in prompts on Saved, Bookings and Profile and make no API calls; Edit profile and Delete account exist only while signed in (conditional navigator groups). |
| Session clear removes protected user data and the query cache, and handles navigation safely | Sign-out, deletion and server-ended sessions clear SecureStore, the TanStack Query cache and app state; the account screens close because their navigator group unmounts. All three paths are tested. |
| OTP and login endpoints reject requests above the configured limits | Per-destination (3 per 10 minutes), resend interval (30 s), daily send cap, 5 attempts per code, plus a per-IP limiter on the auth endpoints. Unit and integration tests cover each. |
| Account deletion revokes sessions and anonymises personal data per the retention policy | Tested in the API (in-app and public routes) against PostgreSQL; policy in `docs/data-retention.md`. |
| Server state is not duplicated unnecessarily in the root reducer | Only the session summary (id, name, roles) is in app state; the profile is a TanStack Query result. Tokens live only in SecureStore. |

On the emulator against the API hosted on IIS: a new user signed in with an SMS code read from the
server log, added a name, and Profile showed the name and number with the Account section. The
database showed one account and one active session for the emulator. On the same API, scripted
checks confirmed that a missing token gets 401, that refresh rotates the token, and that reusing a
rotated token revokes the session.

Checks: Prettier, ESLint (0 problems), `tsc` (0 errors), Jest 160/160 across 17 suites. Backend:
54 unit, 9 architecture and 12 integration tests; the integration tests that need PostgreSQL ran
against a real PostgreSQL 18 database (they skip without `ONEPONDY_TEST_DATABASE`).

Known limitations:

- No SMS or email provider is chosen (D19), so production cannot deliver codes yet. Test servers
  write codes to the log.
- Counters exist (`OnePondy.Auth` meter) but no alerting is wired up for OTP abuse; that needs the
  observability work in a later phase.
- The mobile app sends only plain HTTP in the current test setup; production needs HTTPS.
- English only until reviewed Tamil and French translations exist (D10).

### Phase 3 verification (2026-10-26)

| Acceptance criterion (spec §10, Phase 3) | Result |
|---|---|
| Public APIs return only verified, published places | `/api/v1/places` and `/api/v1/places/{idOrSlug}` query only `is_published AND verification_status = 'verified'`. Any content edit, even to a published place, returns it to draft and takes it off these endpoints until it is verified again. Tested in the API and driven through the console end to end. |
| Every verification records the user, time and source | `Place.Verify`/`ConfirmStillAccurate` require a non-empty source and set the reviewer, timestamp and source together (a database check constraint also enforces this). Shown on the place page and in the audit log. |
| Uploaded images are validated, resized and shown with attribution where required | Images are decoded, oriented from EXIF (metadata, including GPS, is then dropped by re-encoding), and written as three WebP sizes (1600/800/400 px long edge); JPEG/PNG/WebP only, minimum size enforced, non-image files rejected. A licence that is not "owned" or "public domain" requires a credit before the upload is accepted, both in the API and the console form. |
| Console actions are authorized server-side by role; a content editor cannot change configuration | Three ASP.NET Core authorization policies (`content:read`, `content:write`, `administration`) gate every `/api/v1/admin/*` route; the console's own navigation and buttons follow the same role but are not the enforcement. Verified with a live support-agent and content-editor session: write actions return 403, and configuration and staff pages redirect with "denied". |
| Changing a module flag changes what the customer app shows without a new app release | `/api/v1/config` is public and cached for 60 seconds; the app re-reads it on launch and when it returns to the foreground. A module is shown as available only when both the server flag says so and this app version implements it, so a flag can never promise more than the running app can do. The app also blocks use below `minSupportedAppVersion`, with a link to the Play Store listing. |

Driven live end to end (PowerShell script, no JavaScript, against a running API and console):
staff sign-in by email code; create, edit (with a field-level validation error), submit, verify,
publish and re-verify a place; upload a photo and confirm it is served and credited publicly;
hide and restore a module flag and see `/api/v1/config` change; add a support-agent and confirm
their console is read-only with no Staff or Audit link; sign out. All 29 steps passed.

Checks: Prettier, ESLint and `tsc` clean across both app workspaces. Mobile: Jest 173/173 across
19 suites, including new tests for module flags, the minimum-version gate and safe defaults when
the server cannot be reached. Web console: Vitest 15/15 (pure helpers and the place form). Backend:
9 architecture, 118 unit and 18 integration tests, the last of these against a real, freshly
created PostgreSQL 18 database (they skip without `ONEPONDY_TEST_DATABASE`), including that the
`audit_log` table rejects `UPDATE`, `DELETE` and `TRUNCATE` at the database level.

Known limitations:

- D11 (content sourcing) is still open; the schema and console support any source, so this does
  not block the phase.
- No map is shown yet for picking coordinates; latitude and longitude are typed by hand (Phase 4
  brings the map view and PostGIS-backed nearby search).
- Media storage is a local folder on the API server (D8); moving to S3-compatible storage needs
  only a new `IMediaStorage` implementation.
- The console's HTTPS and cookie-security settings were exercised in Development only; `/health`
  equivalents and a real IIS hosting run for the console itself are still to be done, same caveat
  as Phase 2's API hosting.

### Phase 6 verification (2026-10-29)

| Acceptance criterion (spec §10, Phase 6) | Result |
|---|---|
| Two concurrent requests cannot reserve the same exclusive inventory | A slot carries PostgreSQL's `xmin` as a concurrency token and the capacity check runs before the save, so the loser of a race is refused. An integration test fires two parallel requests for the last place, six rounds per run; across three runs every race produced exactly one booking and one `409`, with one place reserved. |
| Retrying a booking request with the same idempotency key returns the original booking | Tested against PostgreSQL: the second call returns the first booking and reserves nothing more. A unique index on customer and key backs it up if two identical requests race. |
| Expired holds release their inventory | The Worker expires pending holds every minute, one save per booking so a lost race never blocks the rest. Tested end to end against PostgreSQL by running the expiry with a clock set past the hold, then booking the freed place. |
| Every booking transition is validated on the server and recorded in history | The state machine is in the domain and rejects every other transition. Each change writes a history row in the same save, and the database rejects `UPDATE`, `DELETE` and `TRUNCATE` on that table and `DELETE` of a booking that has history (tested). |
| The price and cancellation terms shown at booking match the stored snapshot | Both are copied onto the booking when it is made, and the booking screen shows the copy, not the live listing. |

Driven live on the Android emulator against a local API and a throwaway PostgreSQL database:
browse Stays and Activities, open a listing, book a slot with two guests, see it in Bookings, have
the partner accept it through the API, cancel another and see its places freed, save a listing and
find it under Saved, and be refused account deletion with a booking pending (no code sent).

Checks: Prettier, ESLint and `tsc` clean across both app workspaces; Jest 174/174 for the app;
Vitest 15/15 for the console; backend 9 architecture, 207 unit and 40 integration tests, the last
against a real PostgreSQL 18 database (three consecutive runs).

Known limitations:

- **No payment yet** *(superseded by Phase 7, below).* Every booking was treated as free or
  pay-at-venue and a partner confirmed it directly.
- **D13 needs confirming:** Phase 6 says services are discovery-only, but the schema lets a service
  take bookings, and the schema was followed (see the decisions register).
- Listings have no photos (the reference schema has no media for them) and the console has no
  administrator view of all bookings; only partners see their own inbox.
- A business's optional `place_id` link and the PostGIS `location` column in the reference schema
  are not built, the same deferral as for places.
- The app was driven on an Android emulator only; the console's new pages were built and checked
  with lint, types and a production build but not clicked through in a browser.
- A cancelled booking returns its places but nothing refunds or charges anything, by the same rule *(superseded by Phase 7)*.

### Phase 7 verification (2026-10-29)

Phase 7 is built as steps 7.1 to 7.9 (plan and as-built notes: `docs/phase-7-plan.md`). **Payments run in sandbox only**: the
API refuses live keys unless `Payments:Mode` is `Live` and `Payments:AllowLive` is `true`, and neither is set. Refunds are off by
default (`Payments:RefundsEnabled`), push sending is off by default (`Notifications:Enabled`), and the `payments` module flag starts
off.

| Acceptance criterion (spec §10, Phase 7) | Result |
|---|---|
| Payment results are verified server-side; a success reported by the app is never trusted | The webhook is authenticated by an HMAC-SHA256 signature over the raw body, and "is it paid yet?" asks Razorpay itself. The app's pay flow treats the checkout's own report as a hint only: a "cancelled" checkout over a booking the server says is paid shows as paid (tested). |
| Payment failure or timeout does not create a false confirmed booking | A failed event never confirms; expiry asks the gateway first and keeps the hold a grace period if it cannot be asked; a payment after the booking ended is refunded in full (all tested, the races against PostgreSQL). |
| Duplicate webhooks and retried requests are handled idempotently | Events are unique by provider event id, a booking has one open order, ledger transactions are derived from the event and refused if repeated, and a refund uses its own id as the gateway idempotency key. Simultaneous duplicates, simultaneous payouts and simultaneous drafts are tested against PostgreSQL. |
| Reconciliation finds and resolves failed or delayed callbacks | The Worker checks every five minutes and daily, applies a payment or refund whose webhook was lost through the same safe path (recording it), and lists everything else for an administrator. Seeded mismatches are found in the tests. |
| Ledger entries balance for every payment, refund and settlement | A deferred constraint trigger refuses any transaction that does not sum to zero, the table is append-only, and a statement is refused if it disagrees with the ledger. Each event type is tested. |
| Sensitive values are never logged | A webhook's payer details (email, phone, card) are never read or stored, only identifiers, amounts and statuses; keys and secrets are configuration secrets. Reviewed in the code; no log scan was run. |
| Notifications do not expose private booking details to unauthorized recipients | The text is generic, the data is a type and a booking id, the recipient is re-checked when sending, and a phone is used only while its session lives (tested). |

Checks: Prettier, ESLint and `tsc` clean for the app and the console; Jest 262/262 for the app; Vitest 30/30 for the console and a
production build of it; backend 9 architecture, 585 unit and 101 integration tests (6 live-service tests skipped), the last against
a real PostgreSQL 18 database, three consecutive runs.

Known limitations:

- **Driven end to end on a real Android phone (Samsung A14, Android 15), in sandbox.** Against a local API and a scratch database with
  the Razorpay test keys, over a USB tunnel: a customer signed in, saw the "Pay to confirm" card (amount, live countdown), opened Razorpay
  Checkout, paid through Netbanking's demo bank page, and the booking became **Confirmed** with a receipt ("Paid ₹500", the Razorpay
  payment id) and the history updated. On the server the attempt is `captured`, the ledger holds 500 / −450 / −50 and balances, and no
  webhook had arrived (localhost), so it was the app's "is it paid yet?" check that confirmed it. Backing out of Checkout shows "Payment
  wasn't completed, you can try again" and leaves Pay available. The PC's emulator cannot do this: Razorpay Checkout crashes its QEMU
  process every time. **Not seen:** a card payment (the Indian test account refused the 4111 test card as international; netbanking was
  used instead), the webhook path with a real delivery, push on a phone, and the console's new pages in a browser (lint, types, component
  tests and a production build only).
- **Two things the phone run found, both fixed or noted.** Razorpay's Android SDK reports a closed Checkout as code **0**
  (`PAYMENT_CANCELED`) and a network error as 2; the app had them the other way round, so closing Checkout looked like a failure. Fixed,
  with tests, and checked on the phone. The iOS codes are not known here, so on iOS every error shows the "didn't go through" wording.
  Also, the bookings list is cached: a booking that changes elsewhere (the partner accepting it) shows only after the app reloads, and the
  first bookings request right after sign-in can fail once ("Try again" fixes it). Neither is fixed.
- **Push needs an Expo (EAS) project id and a Firebase project**; until then the app quietly does not register for notifications.
  Delivery to a real phone has not been seen.
- **Payouts are recorded, not made.** An administrator transfers the money and records the bank reference; Razorpay Route (D21) and
  GST/TCS on commission wait for the legal and tax review. The commission (10%) is provisional.
- Phase 7 migrations (`AddPaymentsAndLedger` through `AddSettlementRowVersion`) may not yet be applied to the shared development database; apply every pending migration as `onepondy_app`.
- **Secrets to rotate:** the Twilio auth token and the Razorpay test secret were shared in chat during development.
