# Release readiness

This is the spec's release readiness gate (§10, end) worked through for what exists today. It says, task by task, what is **done and checked**, what is **prepared but needs a person** (the owner, a lawyer, a device, an
account), and what is **not possible from code**. Nothing here should be read as "ready to launch": the first three sections of the list below are why it is not.

Status: written 2026-11-03, after Phases 0 to 10 were built. Update it when something changes.

## 1. Where the product stands

| Area | Built | Live-ready? |
|---|---|---|
| Tourism, accounts, itineraries, trip planner (Phases 0 to 5) | Yes, tested | Needs the SMS/e-mail sign-in provider (D19) and a production host. |
| Bookings and online payments (Phases 6, 7) | Yes, tested against the Razorpay **sandbox** | **No.** Needs the legal and tax review, the commission and refund rules signed off, partner agreements and Razorpay live KYC (phase-7-plan.md §9). |
| Food ordering, delivery, cash on delivery (Phase 8) | Yes, tested | **No.** Needs delivery drivers on board, the cash on delivery legal review (phase-8-plan.md), and an FSSAI check of each restaurant. |
| Rides (Phase 9) | Yes, tested, **switched off** (`rides` module) | **No.** Needs the aggregator licence and permits, insurance, verified drivers, a fare tariff and a person watching the safety queue (phase-9-plan.md §5). |
| Reviews, support, abuse protection, operations page (Phase 10) | Yes, tested | Needs the grievance officer named (`Support:GrievanceOfficer`) and someone working the support queue. |

Every module that is not ready stays **off or "coming soon"** through its module flag, and the apps cannot show a module as available that they cannot open. That is how "no feature is presented as live if its
operational dependencies are missing" is met today: the flags are the control, and they are off by default.

## 2. The gate, task by task

| Task | Status | What is left, and who does it |
|---|---|---|
| Critical flows on real devices, including a low-end Android phone | **Not done.** Flows are covered by automated tests (backend 1000+ unit and 230+ integration tests on PostgreSQL; the customer app, driver app and console have their own suites) and the earlier phases were run on the Android emulator. Rides, safety and the shared-trip page have **not** been run on an emulator or a device. | Someone with the devices runs the list in section 4, on a low-end phone too. |
| Accessibility, performance and low-network behaviour against §20 | **Not measured.** The apps use accessible components and tests check labels and roles; nothing has been measured against the targets (TalkBack, 200% font scaling, p95 latency, cold start, crash-free rate). | A person with TalkBack and a mid-range phone; a load test against a staging server (not run: it needs the staging host). |
| Release builds, signing, store listings, privacy policy, Play Data safety form, App Store privacy details | **Not done.** `docs/data-retention.md` lists every kind of personal data kept and for how long, which is the input for the privacy policy and the store forms. | The owner (accounts, signing keys, listings); a lawyer for the policy. Real push needs an Expo project id and Firebase credentials. |
| Publish the backend to IIS, production API URLs | **Documented, not done.** The steps are in `docs/backend-architecture.md` (deployment section) and the README. | Whoever runs the server. |
| Worker as a Windows Service | **Documented, not done.** The Worker runs these jobs: auth data retention, booking hold expiry, food order expiry, ride expiry, refunds, payment reconciliation, notification dispatch, booking reminders, support escalation. | Whoever runs the server. |
| HTTPS, backups, monitoring, recovery drill | **Not done.** The operations page shows what needs attention inside the product, and the API logs, but nobody has checked the logs against the §20 observability target (correlation IDs, alerts on error rate and latency), and there is no external monitoring, no backup job and no drill. WAL archiving (RPO 15 minutes) is not set up. | Whoever runs the server. A drill must be run and recorded before each release (§20). |
| Confirm the §21 compliance items | **Not done: for the owner and advisers.** Open items by phase are in phase-7-plan.md §9, phase-8-plan.md §5, phase-9-plan.md §5 and phase-10-plan.md §5. | The owner, a lawyer and a chartered accountant. |

Acceptance criteria:

- *Critical workflows pass automated and manual regression tests.* Automated: yes. Manual: not done.
- *Production configuration contains no development secrets.* The signing and hashing keys are required settings (the API validates them) and development values live in `.env` and user secrets, not in committed production settings. This has not been checked by deploying a production build. A
  production configuration has not been written, so this is not yet demonstrated. Section 3 lists what it must contain.
- *Backups and restore procedures are tested.* **Partly, by hand, on the development database only (2026-11-05).** `pg_dump -Fc` of `onepondy_dev` was restored with `pg_restore --no-owner --no-acl` into a new database on a separate, empty PostgreSQL 18 server: all 60 tables had the same row counts, and the 27 triggers, 158 check constraints, 100 foreign keys, 201 indexes, 16 functions and 25 applied migrations all came back. Since then `scripts/backup-database.ps1` makes the backup (database plus roles, checked before it counts, old copies pruned only after a good one) and `docs/backup-and-restore.md` has the schedule, the restore steps and a drill log; the full restore with roles was repeated and `onepondy_app` kept its access (it can read and write data, and is refused `create table`). Not done: a scheduled job on the real server, copies kept off the server, WAL archiving (RPO 15 minutes), restoring a production-sized database, and timing a restore against the recovery target. A drill on the production server must still be run and recorded before each release.
- *Deployment and rollback steps are documented.* Deployment: yes. Rollback: migrations are forward-only in practice; `docs/database-architecture.md` lists them, and a rollback plan for a bad release needs to be
  written with the server's owner.
- *No feature is presented as live if its operational dependencies are missing.* Met through the module flags, as above; re-check each flag before each release.

## 3. What a production configuration must contain

The full list of settings is in `docs/backend-architecture.md` ("Settings"). The ones that matter for a first release, and that have no safe default:

- `Auth:Jwt:SigningKey`, `Auth:HashingKey` (new random keys, not the development ones), and the sign-in delivery (`Auth:Otp:Delivery`, with Twilio Verify or MSG91 and DLT registration).
- `ConnectionStrings` for PostgreSQL, using the `onepondy_app` role that **cannot** change the database structure; migrations are applied by a separate, more privileged role.
- `Console:BootstrapAdministrators` for the first administrator only (remove it after).
- `Payments` secrets and the webhook secret, only when `payments` is switched on; `Notifications:Enabled` with the Expo and Firebase credentials on the Worker only.
- `Support:GrievanceOfficer` (the law requires one to be named before launch).
- `Rides` (tariffs, classes, service area, emergency number) before the `rides` module is switched on.
- `Operations` thresholds if the defaults do not suit.
- The apps: `EXPO_PUBLIC_API_BASE_URL` (the customer and driver apps) and `EXPO_PUBLIC_SHARE_BASE_URL` (the customer app: the public address of the console, where shared trips open), and `API_BASE_URL` for the console.
- Module flags: every module off or "coming soon" except what is ready (`config` page of the console).

## 4. Critical flows to run on devices

Run each on a low-end Android phone and one other, with a poor connection for the second pass. Tick them off in a copy of this list.

1. Sign in (SMS code), complete the profile, sign out, sign back in; delete the account.
2. Browse places offline-ish (airplane mode after a first load), save a place, make an itinerary.
3. Book an activity or stay, pay (sandbox), cancel, refund; the partner accepts and rejects.
4. Order food: browse, cart, checkout (online and cash on delivery), track, get it delivered; cancel before cooking.
5. Driver: apply, be approved in the console, go on duty, take a delivery, share position, hand over, confirm cash.
6. Review a completed booking and an order; moderate and reply in the console.
7. Raise a support request from a booking; answer it in the console.
8. **Rides (once the owner switches the module on in a test environment):** a driver applies with documents and is approved; goes on duty and sees a rider's request; the rider sees the driver arrive and the trip
   progress; the rider shares the trip and the link opens in a browser without signing in, then stops when the trip ends; both sides cancel at the allowed points; the emergency button reaches the console's safety
   queue and the operations page; kill the app and the network in the middle of a trip and check it recovers; a driver whose insurance date passes cannot go on duty.
9. The operations page shows what it should after each of the above.

## 5. Things checked in code that the gate cares about

- **Every endpoint is rate limited** by class, per person, and the limits are settings (Phase 10.4).
- **Personal data is minimal and temporary where it can be:** a driver's position is the latest only and erased when the shift or ride ends (the database refuses to keep it); the rider's phone number is never shown
  to a driver; a delivery address is shown to the driver only after accepting.
- **The database repeats the rules that matter** (state transitions, one driver per job, no position kept after a trip, money tables append-only), so a bug in the application cannot corrupt them.
- **Money:** the ledger is append-only and reconciled against the gateway every five minutes; differences are listed for a person and raised on the operations page.
- **Audit:** every staff action, and every opening of personal data by staff, is in the audit log, which cannot be changed.

## 6. Migrations to apply, in order, before running the current code

As the migration role (not `onepondy_app`): everything up to and including `20261103050729_AddRideSafety`. The ones added since the last release candidate are `AddDeliveryJobs`,
`ExtendCustomerHistoryAndAdminRefundReason`, `AddCashOnDelivery`, `AddReviews`, `AddSupportTickets`, `AddRides` and `AddRideSafety`. Full list: `docs/database-architecture.md`.

## 7. Known gaps, so nobody is surprised

- Ride requests choose places from a short list of landmarks in the customer app: there is no map picker yet, and the coordinates of those landmarks are approximate and must be checked by someone who knows the town.
- Ride fares use a straight-line distance and a factor, not a routing provider; there is no online payment, tipping, scheduling or surge pricing for rides.
- An emergency alert reaches the console and the operations page; nothing rings or messages a person. Somebody must be watching the page, or a paging provider must be chosen.
- Driver documents are typed in as numbers and checked by a person against the originals; there is no upload.
- The operations page is a snapshot, with no history, graphs or outbound alerts.
- The rate limiter's counts live in one process; with more than one API server each has its own.
- How long finished rides, safety reports and support requests are kept is an open question for the owner (`docs/data-retention.md`).
