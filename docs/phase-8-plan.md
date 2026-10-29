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
| 8.4 | Customer app: food browse by area, menu, cart, checkout and price breakdown, order status | App tests; run on the phone |
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

Steps 8.1 and 8.2 need none of these answers. From 8.3 on, the defaults above are what gets built.

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
