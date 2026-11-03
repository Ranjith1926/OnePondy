# Phase 9 plan: Transport (rides)

Status: **steps 9.1 to 9.3 (ride drivers, fares, requests, dispatch, the trip, and safety with live updates) built on the server 2026-11-03; the console (9.4) built 2026-11-03; the apps (9.5, 9.6) are planned.** Anything below that is not marked built is still a plan. Spec: Phase 9 (§9), the Ride workflow (§7), Safety (§§ on location and privacy), and the legal table (§21: aggregator
licence, driver verification and insurance).

**Read this first.** Spec Phase 9 is *Release 4* and says "Do not launch publicly until applicable legal and operational requirements are met." This phase builds the software so that the work is not waiting on
code, and keeps it **switched off**: the `rides` module stays off, so the apps keep showing "Coming soon", until the owner has the licence, insurance and verified drivers. Nothing here makes that decision.

## 1. Decisions

These were made without waiting for the owner (they asked for the work to continue). Each is a setting or an easy change; those marked **Owner** need the owner's confirmation before launch (section 5).

| # | Decision | Why |
|---|---|---|
| P1 | **Ride mode is its own approval,** separate from delivery. A person can be an approved delivery driver and not a ride driver. A ride driver has a vehicle class, registration, driving licence, **permit** and **insurance** (policy number and the date it ends), reviewed by an administrator. | Passenger transport needs documents deliveries do not (§21). Changing anything sends an approved driver back to review and off duty, as for delivery. |
| P2 | **Eligibility is checked every time a job is offered or accepted:** approved, insurance not ended, on duty for rides, the vehicle class matches, and not already on a ride. | "Only eligible and approved providers receive relevant jobs." An insurance date that passes takes the driver out of service without anyone remembering to. |
| P3 | **Vehicle classes** are `auto`, `cab` and `bike_taxi`. Which are offered is a setting (`Rides:EnabledClasses`, default `auto` and `cab`). | **Owner:** bike taxis are not allowed everywhere; the default is the safe one. |
| P4 | **The server quotes the fare.** Distance is a straight line between pickup and destination times a road factor (default 1.3); fare = base + per-km × distance, never below a minimum, per class, all settings. A quote is stored, valid for 5 minutes, and a request must carry it, so the fare shown is the fare charged. No surge pricing, no meter. | Spec: "authoritative fare quote from the backend". Straight-line distance is an estimate; a routing provider is a later choice. |
| P5 | **Pay the driver in cash at the end** is the only payment for now. The driver confirms the exact cash taken. Online payment, the platform's commission and the rides cash ledger are not built. | Spec: "cash fares if approved in D3" (**Owner**). Online payment for rides repeats the Phase 7 gate. |
| P6 | **Dispatch is first-to-accept** among eligible drivers on duty whose last position is fresh (under 2 minutes old) and within the offer radius of the pickup (default 5 km). One active ride per driver and per rider, enforced by the database. | Same guarantee as deliveries (8.7): two drivers accepting at once cannot both get it. |
| P7 | **Lifecycle:** `requested → accepted → arrived → in_progress → completed`; the rider may cancel until the trip starts; the driver may give it up until they arrive (it goes back to other drivers) and may cancel after, with a reason; a request nobody takes within 10 minutes expires. No cancellation fee in this version; the screen shows the terms (none). | Terms are snapshotted on the ride like a booking's. |
| P8 | **Location, minimum and temporary.** A driver on duty shares their position only for dispatch; it is kept as the latest only and erased when they go off duty or after 2 minutes of silence it is ignored. During a ride the driver's position is on the ride and shown to that ride's rider only while the ride is active, and erased when it ends. Before accepting, a driver sees the name the rider gave the pickup and how far it is, never the exact point, the note or the rider's name; after accepting, the pickup point, the note and the rider's first name. The rider's phone is never shown to the driver. | Spec: "Location collection follows permission and active-duty rules." |
| P9 | **Safety.** SOS during an active ride (rider or driver) records the moment and position and appears at once on the operations page as an *act now* alert; trip sharing gives a link that shows the ride's status, the driver's first name and vehicle and the driver's live position until the ride ends; an incident report (rider or driver, during or after a ride) goes to a safety queue for support. All of it is audited. | Spec: "Safety (SOS, trip sharing), cancellation, incident reporting". Calling emergency services is the phone's job: the app shows the number and a call button. |
| P10 | **Live updates.** A SignalR hub sends "this ride changed" to the rider and the assigned driver; the apps refetch from the API. After a disconnect they reconnect and refetch, and fall back to polling every 10 seconds while disconnected. The hub carries no personal data and no position. | Spec: "Live updates recover after a disconnect." |
| P11 | **History.** Rides join the customer history view (a new branch of `customer_history`). | Spec: "Extend the customer history view with rides." |

## 2. Guardrails

- **The `rides` module stays off by default.** Everything that starts a ride (a quote, a request) checks it; ride-mode screens say "Coming soon" while it is off.
- Reuse the delivery patterns (row versions, one-active-job indexes, `Result`, audit, rate limit classes). No new frameworks except SignalR.
- Personal data minimised: no rider phone to drivers, no exact pickup before accept, locations temporary, no location history kept.
- Every state change is a method on the entity that refuses an illegal move, and the database repeats the rules (check constraints, unique indexes).

## 3. Build order

| Step | Deliverable | Proof |
|---|---|---|
| 9.1 | Ride driver profile and review: apply, edit, duty, position; administrators approve, reject, suspend, reinstate; eligibility rule (P2). Migration. | An unapproved, suspended, uninsured or off-duty driver is not eligible; changing a document sends an approved driver back to review. **Built (2026-11-03).** |
| 9.2 | Fares, requests, dispatch and the trip lifecycle: quotes, ride requests, offers to eligible drivers, first-to-accept, arrive, start, complete, cancel, expiry (Worker), history view. Migration. | Only eligible drivers see and can accept a job; two accepting at once: one wins; every legal and illegal move; expiry; history lists rides. **Built (2026-11-03).** |
| 9.3 | Safety and live updates: SOS, trip sharing, incident reports, the ride hub, operations alert. Migration. | SOS shows on the operations page; a shared link shows only what P9 says and stops when the ride ends; the hub tells only the two people on the ride. **Built on the server (2026-11-03).** |
| 9.4 | Console: ride driver review, rides list and detail, safety queue; audit wording and filters. | Console tests; a real run. **Built (2026-11-03).** |
| 9.5 | Customer app: ride booking screens (pickup, destination, quote, request, searching, trip, safety, history). Behind the module flag. | Component and screen tests; a real run with a test driver. |
| 9.6 | Driver app: ride mode (apply, duty, offers, trip steps, SOS). | Tests; a real run. |
| 9.7 | Release readiness: what is built, what cannot be done in code, the checklist for the owner. | Documents updated |

## 4. Not in Phase 9

- Online payment for rides, platform commission, driver payouts and the rides cash ledger.
- Routing, ETAs and fares from a maps provider (the fare uses straight-line distance and a factor).
- Scheduled rides, ride sharing, multiple stops, tips, ratings of riders and surge pricing.
- Calling or messaging between rider and driver through the app (a masked number needs a telephony provider).
- Background location on the phone when the app is closed (a store and legal question; the driver app shares position while open and on duty).
- Reviews of drivers (Phase 10 excluded them).

## 5. Open questions for the owner

1. **Aggregator licence and permits.** Is a licence (MoRTH aggregator guidelines as adopted in Puducherry) held, and what must each driver show? The driver form asks for registration, licence, permit and insurance; confirm the list.
2. **Which vehicle classes** are lawful and wanted at launch (default: auto and cab; bike taxi off).
3. **Fare tariff.** Base, per kilometre and minimum for each class (the defaults are placeholders), and whether the government's fare table applies to autos.
4. **Cash only?** Is paying the driver in cash acceptable at launch (D3), and what is the platform's commission on a ride?
5. **Insurance.** What cover must a driver and the platform have, and who checks it?
6. **Safety.** Who answers an SOS and when (the operations page shows it; a person must be watching it, or an alert must be sent to a phone: needs a provider)?
7. **Driver background checks** (police verification) and who does them.
8. **Terms.** Cancellation terms and fees, and the rider and driver terms for the app.

## 6. As built: steps 9.1 and 9.2

Details in [backend-architecture.md](backend-architecture.md) §7.15 and [database-architecture.md](database-architecture.md) (migration 23, `AddRides`). All of it sits behind the `rides` module, which is off.

- **Ride drivers (`RideDriverService`).** A signed-in person applies with a vehicle class, registration, driving licence, permit and insurance policy with its end date; an administrator approves, rejects, suspends or
  reinstates; changing anything sends an approved driver back to review and off duty. **Eligible = approved and insured on the day** (the end date counts as covered, in Indian time); a lapsed policy stops a driver
  starting a shift, and takes them off duty the next time they share a position. One licence and one vehicle belong to one account (unique indexes). Suspending hands back a ride the driver had accepted but not reached.
- **Fares (`RideFare`, `RideOptions`).** A quote is one stored row per offered vehicle class, valid 5 minutes, with the road distance (straight line × 1.3), the fare (base + per-km, never below a minimum, rounded up to a
  rupee), the cancellation terms and whether an eligible driver is close enough (`available`). Places must be named, on Earth, within 40 km of Puducherry, and the trip between 0.3 and 60 km. The settings are checked at
  start-up (`RideOptionsGuard`). The defaults are placeholders for the owner's tariff.
- **Requests.** A request carries a quote of the rider's own that is unexpired and unused; it is refused when no eligible driver is near, when the rider already has an open ride, or when the module is off. The fare is
  the quote's, whatever the tariff says later.
- **Dispatch.** A driver is offered a request when they are eligible and on duty, of the right class, have a position under 2 minutes old, and are within 5 km of the pickup. The offer shows the pickup's name, the
  distance to it, the destination's name, the trip distance and the fare: no coordinates, no rider, no note. Accepting applies the same rule; **the first to accept wins** (row version, a database trigger that refuses a
  second driver, and a unique index of one active ride per driver).
- **Trip.** `requested → accepted → arrived → in_progress → completed`; the rider can cancel until the trip starts; the driver can give up until arriving, and cancel with a reason once arrived; once started the trip can
  only be completed (the driver confirms the exact cash, which must be the fare). A request nobody takes in 10 minutes expires (`RideExpiryJob`, every 30 seconds). Old unused quotes are removed after a day.
- **What each side sees.** The rider: the driver's first name, class and plate, and the driver's position only while the ride is active. The driver: after accepting, the pickup point, the note and the rider's first name,
  never a phone number. A driver's position on the ride and on their profile is the latest only and is erased when the ride or the shift ends (the database refuses to keep it any longer).
- **The database repeats the rules:** check constraints (status, who holds the ride, a time for every step, a position only while active and on Earth, the cash equals the fare), the transition trigger
  (`ride_follows_the_rules`: only the legal moves, a finished ride never changes, the rider, quote and fare never change, rides are never deleted or truncated), and `customer_history` gains a `ride` branch.
- **Account deletion** is refused while a ride is open for the rider or the driver, and removes a ride driver's profile. Finished rides are kept (see the open question in [data-retention.md](data-retention.md)).
- **Tests.** Fare arithmetic and settings; the driver's application, eligibility on the day boundary, review and suspension; quotes and availability; requests; offers; accepting (first wins, the wrong class or
  distance, a lapsed policy, one ride at a time, an expired search); the trip step by step, giving up, cancelling, expiry and what each person sees (unit, 64 tests). Against PostgreSQL: the whole trip over HTTP, access
  control, two drivers accepting at the same moment (five rounds, exactly one wins), one driver accepting two rides, two requests by one rider, and every database rule above (11 tests).
- **Not in 9.1 and 9.2:** location rules in the apps, safety (SOS, trip sharing, incident reports), the live-update hub, the console and the apps: steps 9.3 to 9.6.

## 7. As built: step 9.3 (on the server)

- **SOS.** The rider or the driver can raise one while a driver holds the ride. It records who, which side, and where they were if the phone said (a pair of coordinates, or none). Pressing twice is one alarm (a unique
  index of one open SOS per person per ride). It answers with the emergency number (`Rides:EmergencyNumber`, default 112): OnePondy staff are not an emergency service, and the apps must say so.
- **Incident reports.** Either person, during the ride or up to 30 days after it, with a category (unsafe driving, harassment, accident, vehicle problem, overcharge, other) and words. Only after a driver had taken the
  ride; at most 5 waiting per person.
- **The staff queue** (`/admin/ride-safety`, administrators and support agents): open emergencies first, then the oldest. Opening a report shows the ride, both people and how to reach them, and is audited; taking it and
  closing it (a note is required) are audited and carry the report's version. Reports and their closing notes are never deleted (database trigger).
- **The operations page** has a critical alert for every open SOS and a warning for open incident reports.
- **Trip sharing.** A rider makes a link while the ride is on its way; the secret is shown once and stored only as a keyed hash; a new link replaces the old; valid at most 24 hours; the rider can turn it off. Opening
  it needs no account: it shows the rider's and driver's first names, the driver's class and plate, the two place names and the driver's live position, and once the ride ends, only "ended". Unknown, turned-off and
  old links answer 404, and responses are `no-store`.
- **Live updates.** A SignalR hub at `/hubs/rides` (sign-in required; the token may come as `access_token` in the query because WebSockets cannot carry a header). Each person joins a group of their own, the hub has
  no methods a client can call, and the only message is `rideChanged` with a ride id, sent to the rider and the driver of that ride. The apps refetch on it, on reconnecting, and poll while disconnected.
- **Tests.** Unit (24): every SOS and incident rule, sharing and what the link shows, the queue, audits, the alert. PostgreSQL (4 more): an SOS reaching the queue and the operations page, access control, the shared link
  end to end, the database keeping reports, and the hub requiring a sign-in. PostgreSQL found one bug the in-memory tests could not: the queue ordered emergencies by the stored text, which put incidents first.
- **Not in 9.3:** an SOS that rings or messages a person (needs a telephony or messaging provider and the owner's decision on who is called), a hub that a test drives end to end over a WebSocket (the apps' own
  reconnect handling is tested in 9.5 and 9.6), and sharing a trip with a contact picked inside the app.

## 8. As built: step 9.4 (console)

- **Ride drivers** (`/ride-drivers`, administrators): the list with status filters and an "insurance ended" mark, and a driver's page with the licence, permit and insurance numbers, the phone and email, and approve, reject
  (with a reason), suspend and reinstate. Each acts on the version the administrator saw.
- **Ride safety** (`/safety`, administrators and support agents): the waiting list with emergencies first and a banner while any emergency is open; a report's page with what was reported, a map link to where the
  person pressed the button, the ride, and both people with their phone and email, where staff take the report and close it with a required note. Opening a report is audited.
- **Operations page** links its new "ride emergency" and "incident reports" alerts to `/safety`. The audit log has filters for ride drivers and ride safety, with plain wording for every action and links to the item.
- **Tests.** The safety actions (permission first, versions, the required note, bad ids refused before any call), the view and map-link helpers, and the alert link. Not done: a real-browser run of these pages.
- **Not in 9.4:** a console list of all rides (history for support), which waits for a decision on how long rides are kept (data-retention.md).

