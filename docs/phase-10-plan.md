# Phase 10 plan: reviews, support and trust hardening

Status: **step 10.1 (reviews: rules, moderation, replies and ratings, on the server) built 2026-11-02; steps 10.2 to 10.5 are planned, not started.** Anything below that is not marked built is still a plan. Spec: Phase 10 (§10), the Reviews workflow (§7), the
Consumer Protection (E-Commerce) Rules row in §21 ("no manipulated reviews", a grievance officer) and the abuse-protection items in
§12. The spec says Phase 10 can run alongside Phases 8 and 9 once Phase 7 is complete; Phase 7 and Phase 8 are built, so it can start.
Rides (Phase 9) are not built, so reviews cover bookings and food orders only; rides join when Phase 9 exists.

## 1. Decisions

These are the proposed defaults: what gets built unless the owner says otherwise. Each one changes what customers or staff see.

| # | Question | Proposed default | Why it matters |
|---|---|---|---|
| R1 | What can be reviewed? | A booking that reached `completed`, and a food order that reached `delivered`. One review per transaction, by the customer who made it. Rides later. | The spec's rule: only an eligible completed transaction, only once. Food orders end in `delivered`, not `completed`, so this names them explicitly. |
| R2 | How long after completion? | 30 days (a setting). After that the review window is closed. | The spec says "within a set window" without a number. |
| R3 | What does a review hold? | A rating from 1 to 5 and optional text up to 1000 characters. No photos in v1. | Photos need moderation, storage and licensing (D11); they can follow. |
| R4 | Who is reviewed? | The listing or restaurant, shown on its page. A driver is not reviewed in v1. | Reviewing a driver is a labour and privacy question for the owner. |
| R5 | Moderation | Every review is checked automatically (personal data such as phone numbers and e-mail addresses, links, abusive words from a configurable list) and then goes to a moderator queue; nothing is published without a moderator's decision. | The spec requires an abuse and personal-data check before publishing. Auto-publish is faster but is exactly what the E-Commerce Rules warn about. |
| R6 | Partner replies | One reply per review from the business, shown under the review, after the review is published. A partner cannot edit or delete a review, cannot hide it, and cannot see who wrote it beyond the first name the customer chose. | Spec: partners reply but cannot edit or delete. |
| R7 | Can a customer edit or delete a review? | Edit once within 24 hours while it is still unpublished or published; delete at any time (a deleted review leaves the average). Deleting the account removes the author and the text and keeps the rating anonymously only if the owner agrees (see section 5). | Data protection and the account-deletion promise. |
| R8 | Displayed ratings | An average and a count from published reviews only, shown once a business has at least 3 published reviews. | One review is not a trustworthy average. |
| R9 | Support tickets | A customer or partner raises a ticket (topic, free text, optionally tied to a booking or order they own). Support agents answer in the console. Response-time targets are settings (first reply 24 hours, resolution 5 days); a ticket past its target is flagged and escalates to an administrator. | Spec: response-time targets and escalation. |
| R10 | What a support agent can see | The ticket, the messages, and a short summary of the one linked booking or order (what, when, status, amount). Not the customer's other bookings, phone number or address unless the ticket needs it, and every view of a customer record is audited. | Spec acceptance criterion; also the data-minimisation principle. |
| R11 | Grievance officer | A public contact page with the officer's name and address, filled from settings. The owner appoints the person. | Required by the E-Commerce Rules (§21). |
| R12 | Rate limits | A per-user and per-IP limit on every public and authenticated endpoint group (browse, search, write endpoints, uploads), each a setting, with stricter limits on writes and on review and ticket creation. | Spec: limits cover all public endpoints. Today only authentication endpoints are limited. |
| R13 | Alerts | The console shows operational alerts (rate-limit hits, OTP abuse, moderation backlog, tickets past target); an e-mail or chat webhook is a later step needing a provider. | A dashboard is buildable now; delivery of alerts needs an owner choice of channel. |

## 2. Guardrails (every step)

- The server decides eligibility: a review is accepted only if the transaction belongs to the caller, is in an eligible state, is inside the
  window, and has no review yet. The one-review rule is a database unique index, not only a check.
- Reviews are never edited by staff or partners. A moderator can publish, reject (with a reason the author is shown) or take down a
  published review (audited, with a reason). Nothing is deleted by a partner.
- Displayed ratings come only from published reviews of eligible transactions. There is no import path and no way to create a review
  without a transaction.
- Review text is data, not instructions, and is shown escaped everywhere (apps and console).
- Staff roles are separate: support agents work tickets, content editors do not see tickets, administrators see everything and the audit
  of who looked at what.
- Every moderation, takedown, ticket assignment, escalation and customer-record view is written to the audit log.
- Rate limiting must not lock people out: limits are per user where the caller is signed in, per IP otherwise, with generous defaults
  (many people share one carrier IP), `Retry-After` on every refusal and a logged warning.
- Every step: tests first-class (unit, plus PostgreSQL integration tests for the unique index, ownership and access rules), migrations
  reviewed by hand and applied as `onepondy_app`, docs updated, and a report with files changed, migration details, tests run and results.

## 3. Build order

| Step | Deliverable | Proof |
|---|---|---|
| 10.1 | Reviews domain and API: the review model and states (`pending`, `published`, `rejected`, `removed`), eligibility (R1, R2), one per transaction, the automatic check (R5), the moderator queue API, partner reply API, rating summary on listings and restaurants. Migration. | Only the owner of an eligible, completed transaction can review it, once, inside the window; a published review shows in the summary and a pending one does not; a partner cannot change a review. **Built (2026-11-02).** |
| 10.2 | Reviews in the apps and console: customer app write and read screens and the history entry point; ratings on listing and restaurant pages; console moderation queue and partner replies. | Component and screen tests; a real run of write, moderate, publish, reply |
| 10.3 | Support tickets: ticket and message model, customer and partner creation and reading, agent queue and answer screens in the console, assignment, status, response-time targets and escalation (R9, R10), audited access, grievance officer page (R11). Migration. | An agent sees only what a ticket needs; every record view is in the audit log; a ticket past its target is flagged and escalated |
| 10.4 | Abuse prevention: rate-limit policies for every endpoint group (R12) with settings, per-user partitions, tests that a limit refuses and recovers, and a review of which endpoints are public. | Tests per group; the existing auth limits unchanged |
| 10.5 | Monitoring and alerts (R13): an operations page in the console (limits hit, OTP abuse, moderation backlog, overdue tickets, payment and order attention counts), audit log review filters, and the documentation pass for the phase. | Counts match the data; documents updated |

## 4. Not in Phase 10

- Reviews of drivers, rides (Phase 9) and photos in reviews.
- Machine-learning moderation or translation of reviews.
- Alert delivery by e-mail, chat or paging (needs a provider and the owner's choice).
- Live chat; tickets are asynchronous.
- A customer-facing "was this helpful" or voting on reviews.
- The Phase 7 go-live gate and the legal reviews, which stay with the owner.

## 5. Open questions for the owner

1. Is a 30-day review window right, and should a food order be reviewable at all, or only bookings? (R1, R2)
2. Should every review wait for a moderator, or may clean-looking reviews publish themselves with moderators checking afterwards? (R5; the default is the cautious one.)
3. When a customer deletes their account, are their reviews deleted entirely (the default) or kept anonymously so ratings do not move? (R7)
4. Who is the grievance officer, and what are the support response-time targets? (R9, R11)
5. Where should operational alerts go once a provider is chosen: e-mail, a chat channel, or SMS? (R13)

## 6. As built: step 10.1

Flow in [workflows.md](workflows.md) §26. The decisions in section 1 were built as the proposed defaults, because the owner has not answered the
questions in section 5 yet; each is a setting or a small change if the answer differs.

- **Who may review (R1, R2).** The customer who made a booking that is `completed` or a food order that is `delivered`, within `Reviews:WindowDays`
  (30) of that moment, once. Someone else's transaction answers "not found", so the API does not reveal that it exists.
- **What a review holds (R3).** A rating from 1 to 5 and optional text up to 1000 characters. The author shows as the first name on their account, or
  "A customer" if the name is missing, looks like a phone number or e-mail address. No photos.
- **The automatic check (R5).** `ReviewChecks` looks for phone numbers, e-mail addresses, web links and the words in `Reviews:BlockedWords` (empty by
  default; the operator writes it). It records what it found for the moderator and **never publishes or rejects anything itself**: every review waits
  for a moderator.
- **Moderators.** Administrators and content editors (the existing `content:write` policy). Support agents are not moderators: their role is tickets
  (10.3). A moderator publishes a waiting review, rejects it with a reason the author sees, or takes down a published one with a reason. Each is audited.
- **Replies (R6).** The owner of the business answers a published review once. There is no endpoint to change or remove a review for a partner, and
  a reply cannot be edited.
- **The author's changes (R7).** One edit within `Reviews:EditWindow` (24 hours): the review goes back to waiting (a published one is hidden until it is
  approved again) and any reply to the old words is cleared. The author can delete a review that is waiting or published. A review a moderator
  rejected or took down stays on record (so deleting and posting again cannot get round the decision) until the author's account is deleted, which removes
  all their reviews.
- **Ratings (R8).** The average (one decimal place) and count of published reviews, shown once there are `Reviews:MinCountToShow` (3). It is added to the
  public listing and restaurant views (`rating`, null until then) and to the public reviews page. A bookable listing's rating comes from its bookings;
  a restaurant's from its food orders.
- **API.** `GET /reviews?listingId=` or `?restaurantId=` (public); `POST /reviews`, `GET /reviews/mine`, `PUT` and `DELETE /reviews/{id}` (customer);
  `GET /partner/businesses/{id}/reviews`, `POST .../{reviewId}/reply` (business owner); `GET /admin/reviews?status=` (default `pending`, oldest first),
  `POST /admin/reviews/{id}/publish`, `/reject`, `/remove` (moderators).
- **Database.** Migration `AddReviews`: the `reviews` table with unique indexes (one review per booking and per food order, even when requests arrive
  together), checks for the rating, text length, status, the booking-or-order rule, moderation columns that agree with the status, and a reply that is
  all-or-nothing and only on a published review. No triggers: the rules above are in the service and the checks.
- **Tests.** 62 new unit tests (the text check, the review's own rules, the service for bookings and food orders, moderation, replies, ratings), one for
  account deletion, and four against PostgreSQL (the full HTTP flow, a rating from three reviews and a takedown, six simultaneous reviews of one order
  producing one, and the database refusing rows the service would never write). The earlier suites are unchanged and pass.
- **Not in 10.1:** screens in the customer app and console (10.2); a "can I review this?" marker on the history view (10.2); telling the author or the
  business when a review is published or answered (the notifications gap from Phase 8); readers reporting a review (a takedown is a moderator's decision
  for now); rate limits specific to writing reviews (10.4); photos; reviews of drivers and rides.
