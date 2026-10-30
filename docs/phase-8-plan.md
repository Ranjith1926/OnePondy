# Phase 8 plan: food ordering and the driver app

Status: proposed 2026-10-29. Steps are built one at a time and reported, as in Phase 7. Nothing here switches on real money:
online payment for food stays in sandbox until the Phase 7 go-live gate is met, and the `food` module flag stays `coming_soon`
until then. Spec: Phase 8 (§10), the Food workflow (§7), §21 (legal) and decisions D3, D13, D21, D22.

## 1. Decisions

| # | Question | Proposed default (what is built unless you say otherwise) | Why it matters |
|---|---|---|---|
| F1 | Who delivers? | OnePondy delivery partners through the driver app, assigned by the platform. Restaurants do not deliver themselves in v1. | Decides the whole driver app and assignment design. Needs the **delivery operations plan** the spec lists as a dependency: who the drivers are, how they are paid, insurance, hours. |
| F2 | Cash on delivery | Per restaurant, opt-in, **off** by default, behind a setting; includes reconciling cash the driver collected (step 8.8). | Cash needs a driver cash ledger and daily reconciliation, and legal sign-off. |
| F3 | GST on restaurant orders | Tax is calculated per item from a configurable rate (provisional 5%) and recorded separately from the price on every order line; nothing is invoiced. The spec makes the platform liable for GST on restaurant services (CGST §9(5)). | The rate and treatment need your tax adviser. The schema keeps tax apart so changing it never rewrites history. |
| F4 | Fees | A flat delivery fee and a packaging fee, both settings, both shown before the customer pays. No platform fee yet. | Customer price transparency (Consumer Protection E-Commerce Rules). |
| F5 | Service areas | Named areas (the same list the app uses: White Town, Auroville, ...), not map polygons (D6: no map provider). A restaurant lists the areas it delivers to; `puducherry` means the whole city. | Matches the app's area picker; polygons wait for a map decision. |
| F6 | When does the customer pay? | The restaurant accepts first (a short window, default 5 minutes), then the customer pays (default 10 minutes), reusing the Phase 7 payment, refund and ledger machinery. | Same "accept then pay" rule decided for bookings (D3); avoids charging for orders a restaurant then rejects. |
| F7 | Commission on food | Reuses the provisional `Payments:CommissionPercent`, applied per order, in sandbox. | D21 is still open. |
| F8 | Cancellation and refunds | Customer may cancel until the restaurant starts preparing (full refund); after that, no cancel by the customer, support handles it. Restaurant or system cancel is always a full refund. Refunds stay off by default as in Phase 7. | D22 covers bookings only; food needs its own approved rule. |

## 2. Guardrails (every step)

- Sandbox only for money; the `food` module flag stays off in any real environment.
- The server decides everything that matters: prices are revalidated at checkout, an order's state changes only through the server's
  transition table, a customer cannot move an order's restaurant or delivery state, and a delivery assignment can be accepted by
  exactly one driver (a database rule, not just a check).
- Order creation is idempotent (a key per attempt, unique per customer), as bookings are.
- Menu prices and item names are copied onto the order ("snapshot") so later menu edits never change an existing order.
- Driver location is collected only during an active job (spec §4, Phase 8), never otherwise.
- A restaurant is visible to customers only while its business is approved and active, it has a profile and has switched on
  "accepting orders", and it serves the customer's area. Its FSSAI number is shown on its page (spec §21).
- Every step: tests first-class (unit, plus PostgreSQL integration tests for races and triggers), migrations reviewed by hand and
  applied as `onepondy_app`, docs updated, reported with files changed, migration details, tests run and results.

## 3. Build order

| Step | Deliverable | Proof |
|---|---|---|
| 8.1 | Restaurants and menus: a restaurant profile (prep time, minimum order, delivery areas, open/closed), menu categories and items, partner management API and console pages, a public browse API (restaurants by area, a restaurant's menu) | Unit and integration tests: ownership, visibility rules, item availability, price rules, areas validated |
| 8.2 | Orders domain and server-side quote: cart validation, price breakdown (items, tax, fees), order creation with an idempotency key, the order state machine, restaurant accept / reject / preparing / ready, customer cancel rules | Transition-table tests for every row; duplicate submissions create one order; price revalidated; tax recorded per line |
| 8.3 | Paying for orders: generalise payment attempts, refunds, ledger and statements to orders as well as bookings | The Phase 7 tests still pass; an order pays, refunds and settles; ledger balances |
| 8.4 | Customer app: food browse by area, menu, cart, checkout and price breakdown, order status | App tests; run on the emulator (the Razorpay checkout itself needs the phone) |
| 8.5 | Partner console: order inbox with accept, reject and preparation states, and the restaurant and menu pages | Console tests and build |
| 8.6 | Driver app (new Expo app in `apps/driver-mobile`): sign-in, onboarding, duty status, assignment | App tests; run on the phone |
| 8.7 | Delivery assignment and customer tracking: offers, single-acceptance rule, pickup and drop-off, location only during an active job | Race test: two drivers accept at once, exactly one wins |
| 8.8 | Cash on delivery and cash reconciliation (only if F2 is approved) | Ledger balances; cash collected reconciles |
| 8.9 | Admin order monitoring; extend the customer history view with food orders | Console and view tests |
| 8.10 | Docs: architecture, database and workflow documents, spec rows | Reviewed |

## 4. What I need from you

1. F1: the delivery operations plan (or tell me to build the driver app against a simulated fleet only, as the spec's journey
   allows: "driver accepts a simulated assignment").
2. F2: is cash on delivery in scope for Release 3?
3. F3: the GST treatment, from your tax adviser.
4. F4 to F8: confirm the defaults, or give other numbers.
5. Still open from Phase 7 and needed before any of this goes live: D21 and D22 sign-off, the legal and tax review.

Steps 8.1 and 8.2 need none of these answers. From 8.3 on, the defaults above are what gets built (8.3 is built on them).

## 5. Build notes (as built)

**8.1 (restaurants and menus).**

- **A restaurant is a business of type restaurant** (FSSAI number already required for approval) plus a `restaurant_profiles` row: preparation
  time (5 to 180 minutes), the smallest order, an "open for orders" switch, and `restaurant_delivery_areas`, a set of the same area codes the
  customer app's picker uses (`FoodAreas`; `puducherry` means the whole city). A new restaurant starts **closed**.
- **Menu:** `menu_categories` (unique name per restaurant, at most 30) and `menu_items` (at most 500 active): name, description, price
  (`numeric(12,2)`, more than 0), vegetarian flag, GST rate (provisional default 5%, 0 to 28, per dish), a sold-out switch (`is_available`)
  and `is_archived`. **A dish is never deleted**, because orders will refer to it: it is archived (hidden from customers, unavailable, frozen).
  A category can only be removed if it never held a dish. A database composite key stops a dish pointing at another restaurant's category.
- **Opening for orders needs:** an approved, active business, at least one delivery area, and at least one available dish. A partner can set
  up everything before approval; only opening waits.
- **What customers see:** `GET /api/v1/restaurants?area=...`, `/restaurants/{id}` and `/restaurants/{id}/menu`, without signing in. A restaurant
  appears only while it is open, its business is approved and active, and it serves the area (a restaurant listing the whole city serves every
  area; a customer who has not narrowed the area, or picked the whole city, sees every open restaurant). Closed, suspended and unknown
  restaurants all answer 404, so a closed one is not revealed. The FSSAI number is shown. Archived dishes and empty categories are hidden;
  sold-out dishes show as unavailable. These endpoints are not gated by the `food` module flag (as listings are not): the app does that.
- **Partner API:** `GET/PUT /api/v1/partner/businesses/{id}/restaurant` (the first PUT creates it, later ones need the `version`), `GET .../menu`,
  categories (create, rename and reorder, delete) and dishes (create, edit and move, `/availability`, `/archive`), all owner-only (anyone else gets
  404; a business that is not a restaurant gets 400). Every change except the sold-out switch is in the audit log.
- **Console:** a restaurant's business page gets **Restaurant** and **Menu** buttons: set up and open the restaurant, and manage categories and
  dishes (add, edit, sold out, remove).
- **Migration:** `AddRestaurantsAndMenus` (four tables; no data changes). Apply it as `onepondy_app` like the others.
- **Tests:** 35 unit tests (ownership, restaurant type, profile rules, opening rules, categories, dishes, archive, visibility by area and status,
  audit), 9 PostgreSQL integration tests over HTTP (the whole flow, access control, versions, opening rules, sold out and archive, closing and
  suspending, a simultaneous-creation race, and the database refusing a dish in another restaurant's category and a zero price), 2 console tests.
  The integration suite must start from an **empty database** (the first-administrator tests need it); a filtered run leaves administrators
  behind, so use a fresh one each time.
- **Not done in 8.1:** ordering, carts and prices at checkout (8.2), photos for dishes or restaurants (no media for them yet), dish options and
  add-ons, opening hours by day (a single open/closed switch for now), and the customer app's food screens (8.4).

**8.2 (orders and server-side pricing).**

- **Quote and order:** `POST /api/v1/food/quote` prices a cart; `POST /api/v1/food/orders` places it. The app sends dishes and quantities, **never prices**:
  the server reads them from the menu, works out tax per line (the dish's rate, rounded to the paisa) and adds a flat delivery fee and packaging fee
  (`Food:DeliveryFee` 30, `Food:PackagingFee` 10; fees carry no tax in v1, which is part of the tax review). The same restaurant-visibility rule as the browse
  screens decides whether a restaurant can be ordered from (`OrderService` asks `RestaurantService.FindOpenAsync`).
- **Cart rules:** a specific delivery area (not the whole city); a restaurant that is open and serves it; dishes on *that* menu, available, not archived (named in the
  error); quantity 1 to 20 per dish, at most 30 dishes (a dish listed twice becomes one line); the food total reaches the restaurant's minimum; the order total
  stays under ₹100,000.
- **Placing:** needs the `food` module available, an `idempotencyKey` (same key from the same customer = same order, also under simultaneous retries, enforced by
  `ux_food_orders_idempotency`), an address, a phone, and **`expectedTotal`**, the total the customer was shown. If the menu changed in between, the order is
  refused with `order_price_changed` and the new total. Every dish is copied onto the order (name, price, tax rate), so a later menu edit never changes it.
- **States (`FoodOrder`, tested row by row):** placed → awaiting_payment | confirmed → preparing → ready_for_pickup → out_for_delivery → delivered; rejected,
  cancelled, expired, failed; refund_pending → refunded. The restaurant has `accept`, `reject` (reason), `preparing`, `ready` and `cancel` (reason); the customer
  may cancel from placed, awaiting_payment and confirmed, not once preparing has started (F8); an administrator may cancel anything not yet on its way. Accepting
  confirms directly while online payment is off, and otherwise waits for payment (`Food:PaymentWindow`, 10 minutes). `out_for_delivery`, `delivered`, the refund
  states and `failed` exist in the table now but are moved by steps 8.3 and 8.7.
- **Privacy:** the restaurant's order views leave out the customer's address and phone; only OnePondy's drivers will see them (F1, step 8.7).
- **Time limits:** `Food:AcceptWindow` (5 minutes) for the restaurant to answer; `FoodOrderExpiryJob` (Worker, every minute) expires orders nobody answered or paid for.
- **Migration:** `AddFoodOrders` (three tables, no data changes, and triggers: lines and history are append-only; an order's subtotal and tax must equal the sums of its
  lines at commit). Apply it as `onepondy_app` like the others.
- **Tests:** 37 new unit tests (pricing and rounding, every cart rule, idempotency, price change, snapshots, ownership, restaurant privacy, accept with payment on and off,
  reject, the kitchen steps, cancel rules, stale versions, a lost race, expiry, the whole state table, settings guard) and 10 PostgreSQL integration tests over HTTP (run on a fresh
  PostgreSQL 18 database with the whole suite: 120 passed, 6 live-gateway tests skipped; the full flow, module off, validation, access control, cancel rules, stale version, eight simultaneous identical placements, accept against cancel, six simultaneous
  accepts, and the database refusing edits to lines and history, bad totals and an order with no lines).
- **Not done in 8.2:** paying for an order and refunds (8.3), push notifications about orders, a note to the kitchen (the order has a *delivery* note only), the app
  screens (8.4), the console inbox (8.5), the driver side (8.6 to 8.7), a discount or coupon (the column exists and is always 0).

**8.5 (the restaurant's order inbox in the console).** Flow in [workflows.md](workflows.md) §25.3.

- **Where:** **Orders** on an approved restaurant's page, `/partner/businesses/{id}/orders`. Open orders by default (a new order first, then the kitchen's stages, the longest-waiting
  first within each), with "All orders" and each single status in the filter. A card shows the dishes, total, area, times and the one deadline that matters. A banner counts the
  orders waiting for the restaurant, and the page refreshes itself every 15 seconds while visible, keeping a half-typed reason.
- **Buttons by status** (`lib/foodOrders.ts`, which only decides what to *offer*; the API still checks every move): placed: Accept, Reject; awaiting payment: Withdraw; confirmed:
  Start preparing, Cancel; preparing: Mark ready, Cancel; ready: Cancel only (the driver's step is 8.7). Reject, withdraw and cancel need a reason, which the customer is told.
  Cancelling a paid order while `Payments:RefundsEnabled` is off shows the API's clear refusal.
- **Backend change:** an inbox row (`PartnerOrderSummary`) now carries its **dishes** (the kitchen needs them) and its **`version`** and `estimatedReadyAt`. It still never carries the
  customer's address or phone (tested). An order's dishes are now always listed **by name**: a line has no stored position and time-ordered GUIDs are not strictly ordered inside one
  millisecond, so the order used to be arbitrary (the customer's app showed "coffee" before "dosa" for that reason).
- **Three existing problems found and fixed on the way:**
  1. **The console's Bookings inbox could never accept, reject, cancel or complete a booking.** The API refuses an action whose `version` is not the booking's current one, and the page
     sent none (so 0). `BookingSummary` now carries `version`, the form sends it, and the action refuses to send a made-up one. (Found by reading how the food inbox should get its
     version; it never showed in the tests, which call the API directly.)
  2. **A signed-out partner could not reach the partner sign-in page.** The page sits inside `app/partner/layout.tsx`, which sent signed-out visitors to sign-in, that is, to itself,
     for ever. Found by running the real console. The layout now shows the page alone to a signed-out visitor; everything else is still protected by the proxy and by each page's own API
     calls.
  3. An empty `version` field would have been read as 0 by the new action (`Number('')` is 0); a test caught it before it shipped, and an empty field is now refused.
- **Run for real:** the API (throwaway PostgreSQL 18, scratch keys, no real SMS or gateway) and the production console build, driven by headless Edge over the DevTools protocol with a
  session built from a real API sign-in: the business page showed the Orders button; the inbox listed six orders in their states; the restaurant's page held no customer address;
  pressing **Accept** went through with the real version, redirected with "Order accepted.", the waiting count dropped from 4 to 3, and the order moved to "Waiting for the customer to pay".
- **Not done:** a sound or notification when a new order arrives (only the 15-second refresh), a printable kitchen ticket, opening hours, per-dish sold-out toggling from the inbox,
  and order history or revenue reports.
- **Tests:** 46 new console tests (the rules for what each status offers, the inbox views and their order, the buttons per status, the server action's exact requests and refusals,
  the bookings action's version, the partner layout) for 78 in all; 2 new backend unit tests (692) and extra assertions in two integration tests (128 pass, 6 live-gateway skipped);
  the console's type-check, lint, formatting and production build are clean.

**8.4 (the customer app's food screens).** Flow in [workflows.md](workflows.md) §25.7.

- **Screens (`src/features/food`):** Restaurants (open ones for the picked area, no sign-in needed), a restaurant's menu with a cart, Checkout, and the order screen, all pushed
  from Home's Food shortcut (shown as available only while the server's `food` flag is `available`, via `implementedModules`). Food orders are also listed below bookings in
  the Bookings tab.
- **The cart** is in memory (`CartProvider`, a pure reducer) and holds one restaurant at a time; adding from another asks first. It stops at the server's limits (20 of a dish,
  30 dishes). Whenever a menu is read, gone or sold-out dishes leave it. It holds ids and quantities only, so nothing personal is kept.
- **Money:** the app never sends a price. Checkout shows the server's quote (dishes, GST, delivery, packaging, total) and places the order with `expectedTotal`; a changed menu
  gives `order_price_changed`, the cart is priced again and the new total is shown. One idempotency key per cart and area, reused on retry. The smallest order is checked in the
  app too (the server still enforces it). The customer is told they pay only after the restaurant accepts (F6).
- **Paying reuses the booking payment code.** `paymentFlow` is generic (`payFor`, with `payForBooking` kept as is) and the pay card became `PaymentPanel`, used by bookings and
  orders; the server alone decides whether a payment succeeded. Whatever the checkout reports, the app asks the server.
- **The order screen** re-reads itself while anything is awaited (5 s waiting for the restaurant or payment; 15 s while cooking, on its way or refunding) and shows status in
  plain words, the breakdown, address, licence number, history, ETA, receipt and refunds. Cancel is offered when the server says `canCancel`.
- **Not done:** push notifications about orders (so the app does not ask for permission for them), saved addresses, reorder, dish photos, order tracking on a map (8.7), the
  restaurant console (8.5).
- **Run on the Android emulator** (Pixel 7, API 36, against a local API and a throwaway PostgreSQL 18 database seeded through the real endpoints; sign-in codes from the
  development log, no real SMS or gateway keys): Home's Food shortcut became available from the server flag alone; the restaurant list and menu showed live data (a
  sold-out dish with no add button, veg labels, the licence number); the cart; a guest tapping the cart was sent to sign in and found the cart intact afterwards;
  checkout showed the server's breakdown (₹285 + ₹14.25 GST + ₹30 + ₹10 = ₹339.25, and ₹166 for one dosa) and refused to place until address and phone were filled; the order
  was placed and then, with the restaurant acting through the partner API, **updated by itself** from "Waiting for the restaurant" to "Confirmed" to "Being prepared" (with
  the ETA, and the Cancel button gone once cooking started); the restaurant's own view of the order had no address or phone; with payments switched on, an accepted order showed
  the pay card with a live countdown, and tapping Pay with no gateway configured showed "Payment isn't available right now" instead of failing; the Bookings tab listed both
  orders. The device's installed dev client predated the Phase 7 push work (it lacked `ExpoPushTokenManager`), so it was rebuilt with `expo run:android` first.
- **Found on the emulator and fixed:** a confirmed order that was never paid (online payment off) said "Paid and confirmed"; it now says so only when there is a receipt. The
  bottom buttons sat against the system gesture bar; the food screens now respect the bottom inset.
- **Not proven on the emulator:** Razorpay's own checkout (it crashes on the emulator, so that stays a phone test, as for bookings), cancelling, the price-changed path (unit-tested), and
  how it looks with large system text.
- **Open point for the owner:** with the `payments` module **off**, an accepted order is confirmed with no payment taken, and until cash on delivery exists (8.8, off by default)
  that would mean food delivered unpaid. The same is intended for bookings (pay at the venue), but food has no such step. Before `food` is switched on anywhere real, either require
  `payments` to be available for food orders (a small server check) or approve cash on delivery.
- **Tests:** 62 new app tests (cart rules; area and error helpers; restaurant list; menu and cart, including replacing a cart and dropping sold-out dishes; checkout, including
  the exact request sent, retry with the same key, a changed price, a cart under the minimum, server field errors and the guest; the order screen in each state; food orders in
  the Bookings tab) and updated module and Bookings tests. Whole app suite: 332 tests across 37 suites pass; type-check and lint are clean.

**8.3 (paying for orders, sandbox).**

- **One machinery, two subjects.** A payment attempt, refund, ledger line, statement line and reconciliation exception now belongs to a booking *or* a food order
  (`food_order_id` beside `booking_id`; a database check says exactly one for a payment, refund and statement line, at most one for a ledger line and an exception).
  Nothing was copied: `PaymentService` branches on the attempt's subject, and the webhook, the refund sweep and reconciliation work unchanged from the gateway's ids.
  Booking behaviour, routes, JSON and the Razorpay notes (`booking_id`) and receipt are byte-for-byte as before.
- **Routes:** `POST /api/v1/food/orders/{id}/payment` (the same Razorpay order if asked again, even at the same instant) and `.../payment/refresh` ("is it paid yet?": the
  server asks Razorpay itself). Razorpay's receipt is `fo_<order id>` and its notes name `food_order_id`. Another customer's order is 404.
- **The ledger split (provisional, D21 and F3):** a payment books five lines: `gateway_clearing` (the total); `platform_commission` (`Payments:CommissionPercent` of the
  **dishes only**); `partner_payable` (dishes and packaging, less commission); `delivery_fees` (OnePondy keeps it, its own drivers deliver); `tax_payable` (the GST,
  which OnePondy owes as the e-commerce operator). Two new accounts, so `ck_ledger_account` was widened. A food refund is **always in full** (F8) and reverses the
  capture's lines exactly. Change the split and you change only `LedgerService.PostFoodCaptureAsync`; nothing already booked is rewritten.
- **Cancelling a paid order** plans its refund first and refuses (`refunds_not_enabled`) while `Payments:RefundsEnabled` is off, so the customer's money is never
  silently kept; the cancellation, the refund and `refund_pending` are then saved together and the refund sent after. A payment nothing wants (late, duplicate) is
  always refunded in full, whatever that setting.
- **Closing an order closes its payment:** cancel, reject and expiry mark the order's open Razorpay order expired in the same save, so a payment landing at that instant
  collides with the change and is retried or refunded, never lost.
- **Asking the gateway before expiring:** expiry moved out of `OrderService` into `FoodOrderExpiry` (like `BookingHoldExpiry`). An unpaid order is checked with Razorpay first; a
  payment found confirms it. If Razorpay cannot be asked, it waits `Food:GatewayCheckGrace` (5 minutes, shorter than bookings' 15 because a restaurant is waiting).
- **Statements:** a **delivered** order goes on its restaurant's statement once (its side only), agreeing with the ledger. Until the driver app (8.7) no order can be
  delivered through the API, so this is proven by tests that deliver through the domain.
- **Also fixed (a Phase 7 bug found on the way):** a booking paid twice and then completed made its statement disagree with the ledger for good (the second, refunded payment
  was counted as a sale), blocking every statement for that business. Only the payment that paid the partner counts now (unit test added).
- **Also changed:** account deletion is blocked by food orders in progress (new codes `food_orders_in_progress`, `partner_food_orders_in_progress`, inside the one existing
  guard). The statement CSV's first two headings are now "Reference" and "Item" (it carries bookings and orders). The console's types and statement table accept a null
  `bookingId` and a `foodOrderId`.
- **Migration:** `AddFoodOrderPayments` (columns, checks, indexes, two accounts, two triggers: a payment never changes what it pays for; a refund is for what its payment paid
  for). Only `ADD COLUMN` and a dropped `NOT NULL`: the append-only ledger is never rewritten. **Applied to a database holding Phase 7 and 8.2 data (79 bookings, 47 payments,
  19 refunds, 119 ledger lines): every row kept, and rolled back and forward again.** Apply it as `onepondy_app` like the others.
- **Tests:** 28 new unit tests (27 for food: start, capture, ledger split, duplicates, cancel-while-paying, late payment, expiry with and without the gateway, reconciliation,
  every refund path, the sweep, statements, admin lists, deletion guard; plus one for the booking statement fix), 8 new PostgreSQL integration tests (the HTTP flow; cancel and refund through the real webhook; refunds off; eight
  simultaneous "pay" taps; a payment racing a cancellation five times; the Worker's expiry with the gateway; settlement through the endpoints; the database refusing orphan
  payments, mismatched refunds, a two-subject ledger line, an unknown account and a double settlement). Whole suite: 690 unit, 9 architecture, 128 integration (6 live-gateway
  skipped), all passing on a fresh PostgreSQL 18 database. Console: type-check, lint, formatting and 32 tests pass.
- **Also fixed:** an existing Phase 7 integration test (`The_daily_check_lists_money_at_the_gateway...`) failed whenever the suite ran in the first three hours after midnight UTC,
  because the daily check rightly waits three hours after a day ends. It now runs on a fixed midday clock; what it asserts is unchanged.
- **Not done in 8.3:** push notifications about order payments and refunds, an administrator's cancel of a food order, the app's food checkout (8.4), any partial food refund,
  and a real-device Razorpay run for food (sandbox only, like bookings). **An independent multi-agent review of this diff did not run** (it hit the usage limit); the diff was
  self-reviewed against the Phase 7 invariants instead.

**8.6 (drivers and the driver app).** Flow in [workflows.md](workflows.md) §25.8.

- **Assumption:** the owner did not choose between a delivery operations plan and a simulated fleet, so this follows plan default F1: OnePondy's own drivers, assigned by the platform. A driver is an ordinary
  account with an admin-approved `driver_profiles` row, not a new role. Nothing here depends on a real fleet.
- **Backend:** `DriverProfile` (domain, with the status rules: apply, edit, approve, reject, suspend, reinstate, duty), `DriverService`, `/api/v1/driver/*` and `/api/v1/admin/drivers/*`, audit actions
  `driver.applied|updated|approved|rejected|suspended|reinstated`. Optimistic versions (`xmin`) on every change. Account deletion removes the profile and is blocked while on duty (`driver_on_duty`).
- **Migration:** `AddDriverProfiles` (one new table, with the checks listed in database-architecture.md and a unique licence index). Nothing existing is touched. Apply it as `onepondy_app` like the others.
- **Console:** **Drivers** list (filter by status) and a driver page with approve, reject, suspend and reinstate (`apps/web-console/src/app/(console)/drivers`).
- **Driver app (`apps/driver-mobile`, package `com.onepondy.driver`):** a copy, trimmed, of the customer app's design system, API client, session handling and test setup, so it builds and tests on its own.
  Sharing these as a package between the two apps is a later refactor, not done here. Screens: phone sign-in and code, the application form (also used to change details), and a home screen
  showing in review / changes needed (with the reviewer's reason) / suspended / approved with the duty switch and an honest "no deliveries yet". English only, like the customer app.
- **Not done in 8.6:** photo upload of licence and registration, delivery offers and the single-acceptance rule (8.7), any location collection (8.7, during a delivery only), push notifications to
  drivers, and a driver's earnings.
