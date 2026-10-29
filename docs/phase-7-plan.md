# Phase 7 – Payments, Ledger and Notifications: Implementation Plan

Audience: developers and the product owner. Status: **plan only, nothing built yet.** Spec reference:
`OnePondy_Functional_Specification.md` Phase 7, §7 (booking states), §17 (schema), §20–21.

## 1. Decisions

| Decision | Choice | Status |
|---|---|---|
| D3 Payment gateway | **Razorpay**, UPI + cards + net banking. No wallets. Cash on delivery is a Phase 8 question. | Confirmed |
| D7 Push provider | **Expo Push Service** (Android needs a Firebase project for credentials; iOS later needs Apple credentials, D4). | Confirmed |
| Payment timing | **Customer pays after the partner accepts.** | Confirmed |
| D19 SMS | **MSG91** for production (needs DLT). **Twilio Verify** for development and test servers, already built. | Confirmed |
| D19 Email | Decide later. Email codes and e-mailed receipts stay unavailable until then. | Open |
| Payment window after acceptance | A **fresh 30 minutes from acceptance** (the request hold is replaced, not extended). | **Assumption, please confirm** |
| Go-live | Payments stay in **sandbox** until legal review and an end-to-end settlement test are both done. | Confirmed |

New open decisions this plan needs (to be added to the spec as D21 and D22):

- **D21 Commission and payout model:** what percentage OnePondy keeps, and whether partners are paid by
  Razorpay Route (split at payment time) or by OnePondy through periodic settlements. Route avoids
  OnePondy holding partner money, which matters for the RBI payment-aggregator point in §21, but needs
  each partner to complete Razorpay linked-account KYC. Recommendation: **Route**, subject to legal review.
  Until decided, the ledger records commission and partner payable either way and no real payout happens.
- **D22 Refund rule on cancellation:** proposed from the snapshotted policy: full refund if cancelled at
  least `freeCancellationHours` before the start, otherwise `refundPercentage`% of the total. Rejection,
  partner cancellation and expiry-after-payment always refund 100%.

## 2. Guardrails (apply to every step)

1. **Sandbox only.** A `Payments:Mode` setting (`Sandbox` default). The API refuses to start with live
   Razorpay keys (`rzp_live_…`) unless `Payments:AllowLive` is explicitly true, and that switch is only
   flipped after the go-live checklist in §9. Tests assert the refusal.
2. **Never trust the app.** The app only opens Razorpay Checkout. A booking becomes `confirmed` solely from a
   signature-verified webhook or a server-side fetch from Razorpay (spec rule 9, Phase 7 acceptance criteria).
3. **No secrets in Git or logs.** Key id/secret and the webhook secret live in user-secrets locally and server
   config elsewhere. Card data never touches OnePondy. Log gateway ids and statuses only.
4. **Idempotent everywhere.** Duplicate webhooks, retried requests and replays change nothing a second time.
5. **Money is `numeric(12,2)` INR in the database and `decimal` in code**, converted to integer paise only at
   the Razorpay boundary.
6. **Ledger always balances**, enforced by the database, not only by code.

## 3. Booking flow with payment

Today a partner's "accept" moves `pending → confirmed`. Priced bookings change to:

```mermaid
sequenceDiagram
    participant C as Customer app
    participant API
    participant R as Razorpay
    participant P as Partner
    C->>API: book (pending, 30 min request hold)
    P->>API: accept
    API->>API: pending -> awaiting_payment, new 30 min payment hold
    API-->>C: push: "Your booking was accepted. Pay to confirm."
    C->>API: POST /bookings/{id}/payment (idempotent)
    API->>R: create order (amount in paise)
    API-->>C: orderId, key id, amount
    C->>R: Checkout (UPI / card / netbanking)
    R-->>API: webhook payment.captured (signed)
    API->>API: verify signature, record event once, awaiting_payment -> confirmed
    API-->>C: push: "Booking confirmed"
```

Rules:

- **Which bookings need payment:** total amount above zero while the `payments` module flag is `available`.
  Free listings, and everything while the flag is off, keep today's behaviour (accept → confirmed).
- **Accept** moves to `awaiting_payment` and sets `expires_at = now + 30 min`. Seats stay held.
- **Reject** follows the spec table: allowed from `pending` and from `awaiting_payment`, and it releases the seats. A
  rejection before payment needs no refund; one that races a payment is handled by the late-payment refund (7.4).
- **Cancel** is allowed from `pending`, `confirmed` and, for the customer only, `awaiting_payment` (decided 2026-10-29; the
  spec table now says so). Seats are released at once. The gateway order may still be open, so a payment that lands
  afterwards is recorded and refunded in full (built in 7.2). A partner declines a booking that is awaiting payment with
  Reject, not Cancel.
- **Payment attempts:** a customer may retry; each attempt is a row. An order is created once per attempt and
  reused if the screen is reopened while it is still open.
- **Failure:** a verified failure moves `awaiting_payment → failed` and releases seats, as the spec says; the
  customer can book again.
- **Expiry:** the Worker, before expiring an `awaiting_payment` booking, asks Razorpay for the order's status.
  Paid → confirm; unpaid → expire. A payment that arrives after expiry is **refunded**, or the booking is
  re-confirmed if the seats are still free (spec §17 note).
- **Cancel after confirmation:** `confirmed → cancelled → refund_pending → refunded`, amount from D22.
- **Complete:** unchanged. Completion is what makes a partner payable (settlement, §6).

Domain change: add `AcceptForPayment` (`pending → awaiting_payment`) beside the existing `Confirm`, keep the
transition table in the spec as the source of truth, and add tests for every row of it.

## 4. Schema (Migration 3, `AddPaymentsAndLedger`)

New tables (all money `numeric(12,2)`, status columns with `CHECK` lists, snake_case like the rest):

| Table | Purpose and key constraints |
|---|---|
| `payment_attempts` | One row per Razorpay order: `booking_id`, `provider` (`razorpay`), `provider_order_id` (unique), `provider_payment_id`, `amount`, `currency`, `status` (`created`, `captured`, `failed`, `expired`), timestamps. `xmin` version. |
| `gateway_events` | Every webhook received, raw JSON, `provider_event_id` unique per provider so replays are ignored, `signature_valid`, `processed_at`, `error`. Rows are never deleted by the application. |
| `refunds` | `payment_attempt_id`, `booking_id`, `amount`, `reason`, `status` (`pending`, `processed`, `failed`), `provider_refund_id` unique, requested/processed times. Sum of refunds ≤ captured amount (trigger). |
| `ledger_entries` | Double-entry: `transaction_id`, `account`, signed `amount`, `booking_id`, `description`. A **deferred constraint trigger** requires every `transaction_id` to sum to zero. Append-only like `audit_log`. |
| `settlements` and `settlement_items` | A statement per business per period: items are completed bookings (and refund adjustments), totals, `status` (`draft`, `approved`, `paid`), payout reference. |
| `outbox_messages` | `type`, JSON `payload`, `created_at`, `dispatched_at`, `attempts`, `last_error`, partial index on undispatched. Written in the same transaction as the state change. |
| `device_tokens` (step 7.7 migration, not Migration 3) | `user_id`, Expo token (unique), platform, `last_seen_at`, revoked flag. Removed on sign-out and account deletion. |
| `notification_log` (step 7.7 migration) | What was sent to whom, receipt status, for support and for removing dead tokens. |

Also: extend `customer_history` with payment status, add `payments` to the module keys, and add commission
settings to `app_settings`. Account-deletion guard already blocks on `awaiting_payment`, `confirmed` and
`refund_pending`; settlement items keep the customer row (anonymised) for tax retention.

Rules from the spec still apply: migrations reviewed by hand, run as the `onepondy_app` role (so table
ownership is right), and the integration suite's pending-model-change check stays on.

## 5. Backend components

| Piece | Responsibility |
|---|---|
| `IPaymentGateway` (Application) | Create order, fetch order/payment, refund, verify payment signature, verify webhook. Razorpay implementation in Infrastructure; an `UnavailablePaymentGateway` when keys are missing, like the AI planner and OTP senders. |
| `PaymentService` | Start a payment for a booking the caller owns, return checkout details, enforce one open order per attempt, handle retries. |
| `WebhookService` | `POST /api/v1/webhooks/payments`: read raw body, verify `X-Razorpay-Signature` (HMAC-SHA256 with the webhook secret), insert into `gateway_events` (duplicate id ⇒ 200 and stop), then apply the event in one transaction. Always answers fast; unknown events are stored and ignored. |
| `RefundService` | Compute the amount (D22), create a Razorpay refund, move the booking through `refund_pending`, post ledger entries on `refund.processed`. |
| `LedgerService` | The only writer of `ledger_entries`; builds balanced transactions for capture, commission split, refund and payout. |
| `ReconciliationService` | Compares our records with Razorpay: stuck `awaiting_payment` orders (every few minutes) and the daily settlement report (once a day). Differences become rows on a console "exceptions" screen, never silent fixes. |
| `SettlementService` | Builds statements from completed bookings, approval and mark-paid by an administrator. |
| `OutboxDispatcher` + `ExpoPushSender` | Worker job: read undispatched messages, send through Expo, check receipts, delete dead tokens, retry with back-off. |

Worker additions: `PaymentReconciliationJob`, `OutboxDispatchJob`, and the payment step added to
`BookingHoldExpiry`.

API additions: `POST /bookings/{id}/payment`, `GET /bookings/{id}/receipt`, `POST /webhooks/payments`
(public, signature-protected, excluded from the rate limiter's user policy), `POST/DELETE /devices`,
partner `GET /partner/businesses/{id}/statements`, and administrator endpoints for payments, refunds,
reconciliation exceptions and settlements. The webhook is the only unauthenticated write; everything else
reuses the existing authorization and ownership rules.

## 6. Money, refunds and settlement

- **Capture:** ledger debits `gateway_clearing` for the total, credits `partner_payable` for the partner's
  share and `platform_commission` for ours.
- **Refund:** reverses the matching share of both credits against `customer_refunds`.
- **Completion** makes the partner's share eligible for a statement. Statements list bookings, commission,
  refunds and the net payable.
- **GST:** commission is invoiced to partners; tax treatment (including tax collected at source under §21)
  is a legal/tax item. The ledger keeps tax separate from the amounts above so it can be added without
  rewriting history. Nothing tax-related is switched on until the review.
- **Payout:** under Route, Razorpay settles to partners and the ledger only records it; under manual
  settlement an administrator marks a statement paid with a bank reference. Either way, sandbox only until §9.

## 7. Notifications (Expo push)

- App asks for notification permission only after the first booking, registers the Expo token with
  `POST /devices`, and removes it on sign-out.
- Events: booking accepted (pay now), confirmed, rejected, cancelled, expired, refund processed, and a
  reminder the day before. Partners get "new booking request" in the console area and, if they use the app, by push.
- **Privacy:** the push text is generic ("Your booking has an update"). The payload carries only a booking id,
  and tapping it opens the app, which loads details through the normal authorized API. A token belonging to
  a user who is no longer the booking's owner is never sent a message for it.
- Delivery goes through the outbox, so a status change and its notification either both happen or neither does.
- Needs: an Expo account, a Firebase project with the Android credentials uploaded, and a rebuilt dev client.

## 8. Clients

- **Mobile:** a payment screen opened from the "accepted, pay now" state (and from push), Razorpay Checkout
  via its React Native SDK (a native module, so the dev client must be rebuilt), receipts and payment status
  on the booking detail, refund status after cancellation. The app never decides the result: after Checkout
  closes it polls the booking until the server reports `confirmed` or `failed`.
- **Console:** payments and refunds list, reconciliation exceptions, settlements (draft, approve, mark paid),
  and the partner's own statements page with CSV export.

## 9. Build order, tests and the go-live gate

| Step | Deliverable | Proof |
|---|---|---|
| 7.1 | Migration 3, domain transitions, `payments` module key, config guard | Unit tests for every transition-table row; ledger-balance trigger test on real PostgreSQL |
| 7.2 | `IPaymentGateway` + Razorpay sandbox client, `PaymentService`, payment endpoint | Fake-gateway unit tests; a real sandbox order created |
| 7.3 | Webhook endpoint, `gateway_events`, confirmation, failure, duplicate and bad-signature handling | Integration tests: signed event confirms once, replay ignored, tampered body rejected, failure never confirms |
| 7.4 | Expiry with gateway check, late-payment refund | Worker tests with a controllable clock |
| 7.5 | Refunds and ledger | Refund amounts per D22, partial and full, over-refund impossible, ledger balances |
| 7.6 | Reconciliation job and exceptions | Seeded mismatches are found and listed |
| 7.7 | Outbox, `device_tokens`, Expo sender | Delivery after commit, retry, dead-token cleanup, no private data in text |
| 7.8 | Mobile payment screen, receipts, push | Emulator run against Razorpay sandbox (test UPI/card) |
| 7.9 | Console payments, settlements, partner statements | Console run, statement totals match the ledger |
| 7.10 | Docs: update the architecture, database and workflow documents, spec rows | Reviewed |

**Go-live gate** (all required before `Payments:AllowLive` is ever set): legal and tax review complete (§21),
D21 and D22 signed off, partner agreements in place, Razorpay live-account KYC complete, an end-to-end
settlement exercised in sandbox with a statement that reconciles to the ledger and to Razorpay's report, a
tested refund path, monitoring and alerts on failed webhooks and the outbox backlog, and the DLT-registered
MSG91 sender live so real users can sign in.

## 10. What I need from you

1. Confirm the **30-minute payment window** after acceptance, or give another duration.
2. A Razorpay account in **test mode**: the key id and secret and a webhook secret, set as user-secrets by you
   (`Payments:Razorpay:KeyId`, `KeySecret`, `WebhookSecret`). Never paste them in chat. The webhook also needs a
   public URL later; for local work I will use signature-verified test payloads and Razorpay's own simulator.
3. A view on **D21** (Route vs manual payouts, commission %) and **D22** (the refund rule above). I can build
   with the defaults and make them settings, but the numbers are yours.
4. Start the legal and tax review in parallel and the DLT registration for MSG91.
5. Later, for push: an Expo account and a Firebase project.

Steps 7.1 to 7.7 need none of the third-party accounts except step 7.2's sandbox order, so I can start
building as soon as the payment window is confirmed.

## 11. Build notes (as built)

These record where what was built differs from, or settles a question in, the plan above.

**7.2 (payment start).** Cancelling while `awaiting_payment` is allowed for the customer (spec table updated). One open
payment attempt per booking is a database rule (`ux_payment_attempts_open_booking`). A capture is accepted for an
attempt in any state except already captured, because a customer can still pay an order we had given up on.

**7.3 (webhook), `PaymentWebhookService` and `POST /api/v1/webhooks/payments`.**

- **Authentication is the signature.** HMAC-SHA256 of the exact raw body with `Payments:Razorpay:WebhookSecret`, compared in
  constant time. A missing or wrong signature answers 400 and is **not stored**, so an unauthenticated caller cannot fill the
  table. With no secret configured the endpoint answers 503.
- **Each event is applied once.** `X-Razorpay-Event-Id` is unique in `gateway_events`. A redelivery of a finished event answers
  200 `duplicate`. An event that was stored but never finished (the server stopped part way) is applied on the next delivery;
  applying is idempotent. Two identical deliveries at the same instant produce one confirmation (tested against PostgreSQL).
- **Answers the gateway acts on.** 2xx stops retries: processed, duplicate, ignored, and "needs attention" (valid but not
  applicable, such as an unknown order or a wrong amount, stored with the reason for a person to review). 409
  `payment_busy` asks the gateway to deliver again, used when the booking changed at the same instant.
- **Stored payload is sanitised, a change from "raw JSON".** Razorpay's payment event carries the payer's email, phone number
  and card or UPI details. Only the event type, payment, order and refund ids, amount, currency, status, method and error code
  are kept. This keeps personal data out of the table (DPDP) and nothing needs the rest: reconciliation (7.6) asks Razorpay
  directly.
- **`payment.failed` does not fail the booking.** Razorpay lets a customer retry on the same order until the window ends, so
  failing the booking on the first failed try would turn a later successful retry into a refund. The failed try is recorded,
  and the booking ends by payment or by expiry. The domain's `Fail` move stays available for a different rule if wanted.
- **Events handled:** `payment.captured` and `order.paid` (confirm, or refund in full if the booking no longer wants the
  money), `payment.failed` (record), `refund.processed` (mark the refund processed, and a cancelled or rejected booking becomes
  `refunded`), `refund.failed` (mark failed, booking keeps waiting). Others are stored and ignored. Only `captured` payments in
  INR for exactly the amount due are applied.
- The webhook works whether or not the `payments` module flag is on: a payment already made must always be recorded.

**7.4 (expiry with a gateway check).**

- **The Worker asks before it expires.** For a booking that was waiting for payment, `BookingHoldExpiry` first asks Razorpay
  for the payments on each of the booking's orders (`GET /orders/{id}/payments`). A captured payment is applied exactly as a
  webhook would apply it, so a customer who paid at the last moment keeps the booking. Bookings the partner never answered, and
  bookings nobody started paying for, expire without calling the gateway.
- **"Could not ask" is not "not paid".** If Razorpay cannot be reached, or the answer cannot be applied (wrong amount, another
  currency), the booking is left alone for now and retried every minute, up to `Payments:GatewayCheckGrace` (default 15
  minutes) past its deadline. After that the hold is released anyway, so a long outage cannot hold seats forever, and a payment
  that still turns up is refunded in full by the 7.2 logic. The job logs a warning while bookings are waiting, so an operator
  sees the gateway problem.
- **Orders are closed with the booking.** Cancelling, rejecting or expiring a booking now marks its open payment attempts
  `expired` in the same save, so a finished booking never keeps an open order (and a new payment is refused). A payment that
  still lands on a closed order is accepted and refunded. If that payment arrives in the same instant, the two saves collide on
  the attempt's row version and one retries, so neither is lost.
- **Late payment is refunded, not re-confirmed.** Spec §17 allows "refunded, or re-confirmed if the inventory is still
  available". Expired is a final state in the spec's table, and re-confirming would reopen it and could oversell a slot that
  was released and rebooked, so the refund option is the one built. Re-confirmation can be added later if the business wants it.
- **Checked end to end.** An integration test runs the Worker and the webhook at the same instant, six rounds, and requires one
  consistent ending every time: paid and kept with no refund, or expired with the full amount refunded.

**7.5 (refunds and the ledger).**

- **What a cancellation refunds** (`RefundCalculator`, the proposed D22 rule): the customer cancelling gets everything back up to
  the listing's free-cancellation deadline and the listing's refund percentage after it; a partner cancelling always refunds
  everything. The deadline and percentage are each listing's own terms, copied onto the booking when it was made, so the
  partner sets them per listing. A policy that returns nothing leaves the booking `cancelled` with no refund.
- **Off by default.** While `Payments:RefundsEnabled` is `false`, cancelling a *paid* booking is refused (503
  `refunds_not_enabled`) and nothing changes, rather than cancelling it and quietly keeping the customer's money. Free
  bookings, and bookings not yet paid, cancel as before. Refunds of late or duplicate payments (7.2 and 7.4) are not held back:
  that money should never have been kept.
- **One save.** The cancellation, the `refund_pending` move, the seats being given back and the refund record are saved
  together; only then is Razorpay asked. If Razorpay cannot be reached the refund stays recorded without a gateway id and a new
  Worker job (`PendingRefundJob`, every minute) sends it once it is two minutes old, so a refund is never lost waiting for a
  webhook that will not come.
- **No double refunds.** The refund's own id is sent as Razorpay's `X-Refund-Idempotency` header, so a repeat after a lost
  answer returns the first refund. (From Razorpay's documentation; not exercised against the live sandbox, which needs a real
  checkout payment.) Our own database also refuses refunds that add up to more than was paid, and a customer and a partner
  cancelling at the same instant produce exactly one refund; the other gets 409 `booking_changed`.
- **The ledger** (`LedgerService`, the only writer). A payment that confirms a booking: gateway_clearing debit, the commission
  and the partner's `partner_payable` credit. A payment no booking wants: gateway_clearing debit, `customer_refunds` credit.
  A refund the gateway reports paid: the reverse, in proportion, using the commission actually booked at the time, not today's
  rate. Every business event is one transaction that adds up to zero (the database refuses otherwise), is saved with the change
  it describes, and has an id derived from the event, with a unique (transaction, account) rule so booking an event twice is
  recognised and impossible to double count.
- **Not here yet:** statements and settlements to partners (7.9), reconciliation against Razorpay's reports (7.6), refund status in
  the app (7.8). The commission is a provisional 10% for sandbox only.

**7.6 (reconciliation).**

- **What runs.** `PaymentReconciliationJob` in the Worker, every five minutes, plus a daily comparison. The webhook stays the normal
  path; this is the net under it for webhooks that are late or lost. With no Razorpay keys the gateway checks simply report
  "could not be reached" and raise nothing.
- **Every five minutes** (`RunFrequentChecksAsync`): orders created in the last two hours that are not recorded as paid are asked
  about at the gateway; refunds the gateway accepted ten or more minutes ago are asked about; stored webhooks that could not be
  applied or never finished (after 15 minutes) are listed; payments and refunds recorded as done must have ledger entries.
- **Once a day** (`RunDueDailyChecksAsync`, per UTC day, three hours after the day ends so late webhooks have arrived). The
  gateway's payments for the day (read-only `GET /payments`, with six hours either side) are compared with ours both ways:
  money at the gateway we do not know or disagree about, our captured payments the gateway does not show, refund totals, and
  the whole ledger adding up to zero. A day whose check could not run is retried, not skipped; after an outage it catches up in
  order, at most a week per run; progress is kept in `app_settings` (`payments_reconciled_through`). If the gateway's list is longer than
  one fetch (5,000 payments), the "missing at the gateway" half is skipped and logged rather than guessed.
- **What it fixes by itself, and what it only reports.** A payment or refund the gateway says is complete, that no webhook told
  us about, is applied by exactly the path a webhook takes (same checks, same ledger entries, same refund of a late payment) and
  recorded as `auto_resolved`, so a lost-webhook problem is still visible. Everything else (wrong amount, unknown payment, missing
  payment, refund failed or stuck or never sent, ledger gaps) becomes an `open` exception for a person. Nothing is silently changed.
- **`reconciliation_exceptions`** (migration `AddReconciliationExceptions`). One row per problem, by a fingerprint that is unique
  whatever the status: seeing it again only counts the sighting, and a problem a person resolved is never re-opened.
  The database refuses an inconsistent row: open exactly while it has no closing time, and a person's resolution needs a person and a note.
- **Administrator API** (`Administration` policy only): `GET /api/v1/admin/payments/exceptions` (filters `status`, `kind`, paging),
  `GET .../exceptions/open-count`, `POST .../exceptions/{id}/resolve` with a note (audited). The console screen for these comes with
  the other payment screens in 7.9, so for now an administrator uses the API.
- **Alerting.** The Worker logs a warning every run while problems are open, and whenever the gateway cannot be reached; an alert on
  those log lines is the monitoring hook.
- **Not done, deliberately.** Comparing Razorpay's *settlement* report (fees, bank reference) needs payouts, which stay unbuilt
  until legal review (Route or manual settlement, D21); this step compares the payments themselves. Days are UTC, not India time.

**7.7 (push notifications through the outbox, Expo).**

- **The outbox.** A booking change and the notification it causes are saved together (same transaction), so either both happen or neither
  does: a new request tells the business's owner; accepting tells the customer to pay (or that it is confirmed, for free bookings);
  rejecting, cancelling, a hold expiring, a verified payment (customer: confirmed, partner: paid) and a processed refund each tell the
  right person. The person who made the change is not told about it. Completing a booking sends nothing. A reminder is queued about
  a day before each confirmed booking starts, once (a unique key stops a repeat).
- **The Worker sends them** (`OutboxDispatchJob`, every 20 seconds; `BookingReminderJob`, every 30 minutes). A message is taken
  with a short lease and a row version, so two dispatchers can never send the same one. A failure is retried after 1 min, 5 min, 30
  min, then 2 h, and given up on after five attempts (logged as an error). A message older than 24 hours is dropped, not delivered
  late. If nothing is registered for the person it is simply finished.
- **Privacy, enforced at sending time, not only at queueing.** Before each send the recipient is checked again: a customer message
  goes only to that booking's customer, a partner message only to the business's owner, never to a closed account. A phone is used only
  while the session that registered it is still signed in (sign-out, expiry, reuse detection, account deletion and a role change all
  end it), with nothing for the app to remember to do. A phone registered by someone else later belongs to them, not the previous user.
  The words are generic ("Your booking is confirmed."): no name, amount, listing, date or place, and the only data sent with a
  notification is its type and the booking id; the app opens the booking and loads details through the normal sign-in-protected API.
  Reminders and "pay now" messages are dropped if they are no longer true of the booking.
- **Delivery receipts.** Expo accepting a message is not the phone showing it, so about 15 minutes later its receipt is read: delivered, or
  failed. A phone Expo says no longer has the app (`DeviceNotRegistered`) is deleted straight away, at the ticket or at the receipt. With
  no receipt after a day the message is recorded as failed.
- **Tables** (migration `AddPushNotifications`): `device_tokens` (one row per phone, unique token, removed with its session or its
  user; max 10 phones per account), `notification_log` (type, status and Expo's ticket, never the message text), and two columns on
  `outbox_messages` (`next_attempt_at`, `dedupe_key`). Instead of a "revoked" flag, a dead or signed-out phone is deleted.
- **API:** `POST /api/v1/devices` (token and platform, tied to the caller's session; returns the device id) and
  `DELETE /api/v1/devices/{id}`. The app side (asking permission after the first booking, registering the token, opening the booking
  when a notification is tapped) is part of 7.8.
- **Off by default.** `Notifications:Enabled` is `false`: messages are still queued, but nothing is sent. Turn it on once the Expo
  project is ready (below).
- **English only for now.** The app has no other language yet; the templates fall back to English and take another language when it exists.
- **Retention.** The Worker removes notification log rows and finished outbox messages after 30 days (docs/data-retention.md).
- **Needs before real delivery:** an Expo account and project (and, if its enhanced push security is on, an access token set as the
  secret `Notifications:Expo:AccessToken`), a Firebase project with the Android credentials uploaded to Expo, the app built with
  push enabled, and a real phone. Verified here against Expo's real API with a made-up token (nothing delivered) and with a stub.

**7.8 (the app side of payments and notifications).**

- **The app never decides whether a payment succeeded.** The booking detail shows a "Pay" card while the booking is
  `awaiting_payment`, with the amount and a countdown to the end of the 30-minute window. The button asks the server for a gateway
  order (`POST /bookings/{id}/payment`), opens Razorpay Checkout (the React Native SDK, `react-native-razorpay` 3.0.0, a native
  module, so the development build must be rebuilt once), and when Checkout closes asks the server what happened.
  Whatever Checkout reported, only the server's answer counts: a "cancelled" Checkout over a booking the server says is paid shows
  as paid. After a reported payment the app asks up to six times, two seconds apart, then says "we're confirming your payment";
  the booking screen also re-reads itself every 5 seconds while it waits for payment.
- **New endpoint: `POST /api/v1/bookings/{id}/payment/refresh`** ("is it paid yet?"). For the booking's own customer only (anyone
  else gets 404, no sign-in 401), and only while the booking is `awaiting_payment`, the server asks Razorpay for the order's payments
  and applies a captured one exactly as the webhook would (same idempotent path), then returns the booking. It means a slow or
  missing webhook (always the case on a laptop, which Razorpay cannot reach) never leaves a customer stuck.
- **Receipts and refunds on the booking detail.** `GET /bookings/{id}` now also returns `payment` (amount, currency, paid-at, the
  gateway's payment id as the reference, or null) and `refunds` (amount, reason, status, requested and processed times). A refund
  the team is still sorting out (`failed`) is shown to the customer as "on its way". Cancelling a paid booking shows the refund rule
  that applies before confirming. A booking in `refund_pending` is re-read every 15 seconds.
- **Notifications.** After the customer's first booking the app asks once for permission to notify (never earlier, never twice). On
  each sign-in, only if permission was already given, the phone registers its Expo push token (`POST /devices`); on sign-out it
  unregisters (best effort; the server also stops using a phone when its session ends). A notification tap opens that booking, but
  only for a signed-in customer and only for a known type with a well-formed booking id; the booking still loads through the
  signed-in API, so a forged link reveals nothing. While the app is open a notification shows as a quiet banner.
- **Push needs an Expo project.** An Android phone cannot get an Expo push token without an EAS `projectId` (and Firebase). Until that
  exists in the app config, registration quietly does nothing ("unavailable"), so the rest of the app is unaffected.
- **Not yet verified on a real checkout.** Razorpay's "customer closed Checkout" error code is taken as 2 (its documented
  Android value); confirm it on a device with a test card, because a wrong value would show a closed Checkout as "failed" rather
  than "cancelled" (harmless, only the wording differs).
- **Tests.** Backend: 11 unit tests (receipt, refunds, refresh) and 4 integration tests (owner confirms through the refresh, a stranger
  and anonymous are refused, a booking not awaiting payment is returned untouched, a late payment after cancelling shows its refund).
  App: the pay flow (every outcome, polling, server over checkout), the Checkout error mapping, push registration (every branch),
  link validation, the pay card, receipt and booking detail, error messages and the refetch rule.

**7.9 (console payments, statements and partner statements).**

- **A statement is a view of the ledger.** `SettlementService` builds one per business and period (UTC days; a period must have
  finished). Every amount comes from the ledger: gross sales, the commission kept, refunds to customers, and the net payable, which
  equals what the ledger says the partner is owed. For each booking the statement arithmetic is checked against the ledger's
  `partner_payable` balance for it, and if they disagree nothing is drafted (`settlement_ledger_mismatch`).
- **Which bookings.** A booking is on a statement once it is `completed`, or `refunded` after a partial refund (the partner keeps the
  part the customer was not given back, for example after a late cancellation). A booking refunded in full, or never paid online,
  owes the partner nothing and never appears. A booking is a sale once and a refund at most once (a database rule), so no statement
  counts it twice; a refund that arrives after its sale was already settled is carried on a later statement as an adjustment. A
  statement needs a positive net: otherwise `settlement_nothing_due` and the bookings wait for a later one. At most 500 bookings
  per statement; the rest wait.
- **Draft, approve, pay.** Draft is the administrators' working copy (the partner cannot see it) and can be discarded, which
  returns its bookings to the pool. Approving makes it visible to the partner. **Marking paid** needs the bank reference (UTR),
  and posts the payout to the ledger (`partner_payable` down, `partner_paid` up) in the same save. Nothing approved or paid can be
  undone. Payouts themselves are still made outside the system; the system only records them.
- **Concurrency.** Statements carry a row version (migration `AddSettlementRowVersion`; `xmin`, so no new column), the payout
  ledger transaction id comes from the statement, and the ledger refuses a second one. Eight administrators paying the same
  statement at once pay it once; the rest get 409. A save that loses such a race can surface from EF as a `DbUpdateException` or,
  when PostgreSQL picks it as a deadlock victim, as an `InvalidOperationException` wrapping one: `WriteConflicts.Is` recognises both.
  **The payment services of 7.3-7.7 still catch only `DbUpdateException`**, so the same rare deadlock there would answer 500
  instead of 409 (nothing is lost; the webhook is retried). That is a known follow-up.
- **API.** Administrators: `GET /api/v1/admin/payments`, `/refunds` (status filter, paging; no customer details, only the booking
  id), `GET/POST /admin/settlements`, `GET /admin/settlements/{id}`, `POST .../{id}/approve`, `.../paid`, `.../discard` (each with the
  row `version`). Partners: `GET /api/v1/partner/businesses/{id}/statements`, `/{statementId}` and `/{statementId}/csv`, for
  approved and paid statements of their own business only (anyone else gets 404). Every step is in the audit log.
- **CSV.** UTF-8 with a byte-order mark (so Excel reads it); text typed by partners that begins with `=`, `+`, `-`, `@` is defused
  against spreadsheet formula injection, while amounts keep their minus sign.
- **Console.** Administrators get **Payments** (with Refunds and Reconciliation tabs; the reconciliation list opens on what needs
  attention and each item is closed with a note) and **Statements** (list, draft form, detail with approve, mark paid and discard).
  Partners get **Statements** on their business page, a statement detail and a CSV download that goes through the console, so the
  browser never holds the access token.
- **Not done.** Razorpay Route payouts (D21), GST/TCS on commission (waits for the §21 tax review), and per-listing commission.
  The console screens are covered by build, type-check and component tests, and were not browsed against a running API.
- **Tests.** Backend: 26 unit tests (amounts tie to the ledger for a full sale and for a late cancellation, eligibility, periods,
  discard, approve and pay, partner visibility, CSV), 3 for `WriteConflicts` and 6 integration tests against PostgreSQL (the full HTTP
  flow with the ledger checked, access control, simultaneous payouts and simultaneous drafts). Console: label coverage, query string,
  statement table and badge.

**7.10 (documentation).** `docs/backend-architecture.md`, `docs/database-architecture.md` and `docs/workflows.md` now describe Phases 1 to 7
(the database document was regenerated from a database built by applying all thirteen migrations: 40 tables), `docs/data-retention.md`
covers statements, and the README and the specification's status, D21 and Phase 7 entries say what is built. They also say plainly what
is **not**: live payments, Route payouts, GST/TCS, push delivery to a real phone, and a device run of the payment flow.
