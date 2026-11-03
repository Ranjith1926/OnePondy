# Phase 10 plan: reviews, support and trust hardening

Status: **step 10.1 (reviews: rules, moderation, replies and ratings, on the server) built 2026-11-02; step 10.2 (reviews in the customer app and the console) built 2026-11-03; step 10.3 (support requests: the server, the customer app and the console) built 2026-11-03; step 10.4 (rate limits on every endpoint) built 2026-11-03; step 10.5 (operations page and audit log filters) built 2026-11-03. Phase 10 is built, apart from what stays with the owner (section 5).** Anything below that is not marked built is still a plan. Spec: Phase 10 (§10), the Reviews workflow (§7), the
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
| 10.2 | Reviews in the apps and console: customer app write and read screens and the history entry point; ratings on listing and restaurant pages; console moderation queue and partner replies. | Component and screen tests; a real run of write, moderate, publish, reply. **Built (2026-11-03).** |
| 10.3 | Support tickets: ticket and message model, customer and partner creation and reading, agent queue and answer screens in the console, assignment, status, response-time targets and escalation (R9, R10), audited access, grievance officer page (R11). Migration. | An agent sees only what a ticket needs; every record view is in the audit log; a ticket past its target is flagged and escalated **Built (2026-11-03).** |
| 10.4 | Abuse prevention: rate-limit policies for every endpoint group (R12) with settings, per-user partitions, tests that a limit refuses and recovers, and a review of which endpoints are public. | Tests per group; the existing auth limits unchanged **Built (2026-11-03).** |
| 10.5 | Monitoring and alerts (R13): an operations page in the console (limits hit, OTP abuse, moderation backlog, overdue tickets, payment and order attention counts), audit log review filters, and the documentation pass for the phase. | Counts match the data; documents updated **Built (2026-11-03).** |

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
- **Not in 10.1:** screens in the customer app and console (built in 10.2, section 7); a "can I review this?" marker on the history view (not built: the review is offered on the booking's or order's own screen); telling the author or the
  business when a review is published or answered (the notifications gap from Phase 8); readers reporting a review (a takedown is a moderator's decision
  for now); rate limits specific to writing reviews (10.4); photos; reviews of drivers and rides.

## 7. As built: step 10.2

Flow in [workflows.md](workflows.md) §26. No server change: everything below uses the 10.1 API.

- **Customer app** (`features/reviews`: API client, hooks, error messages, `StarRating`, `RatingLine`, `PublicReviews`, `MyReviewSection`, `ReviewFormSheet`).
  - A **completed booking's** screen and a **delivered order's** screen offer "Write a review". Nothing is shown, and the server is not asked, while the booking is not
    completed or the order not delivered.
  - Once written, the same place shows the review with its stars and words, what happens next (waiting to be checked; published; not published, with the moderator's
    reason; taken down), the business's reply, "Change review" while the author still has their one change (the server says so with `canEdit`; the app does not guess the
    time limit) and "Delete review" (with a confirmation) while it is waiting or published.
  - A **listing's** page (bookable listings only) and a **restaurant's** page show the rating under the name, once there are three published reviews (the server leaves it out
    until then), and the published reviews with the business's reply under each (the first page of 25). If the reviews cannot be loaded the section is simply not there.
  - Errors are answered in words (`review_window_closed`, `review_already_exists`, and so on), in English only like the rest of the app until translations are reviewed.
- **A fault found on the emulator and fixed:** the shared bottom sheet did not move up when the keyboard opened, so the review's text box and Submit button were hidden behind it.
  `ModalSheet` now lifts itself above the keyboard (`KeyboardAvoidingView`), which helps every sheet with a field.
- **Console.** A **Reviews** page for administrators and content editors (a new link in the menu for those roles): tabs for waiting, published, rejected and taken-down reviews,
  each review with its stars, business, listing or "Food order", the author's first name, the automatic check's findings as labels ("Contains a phone number"), the words, the
  business's reply, and Publish, Reject and Take down (the last two ask for the reason the author is shown). Each decision carries the version the moderator was looking at,
  so a review that changed meanwhile is refused instead of decided on words not read. A business owner's **Reviews** page (a new button on an approved business) lists its published
  reviews and gives each one reply form, which says it is sent once and cannot be changed. A shared `lib/ids.ts` reads ids and versions from forms (a GUID or a whole number,
  never an empty value turned into 0).
- **Tests.** Customer app 390 (43 new: star rating, the author's section in every state, the public list and rating line, error messages, and the review parts of the booking,
  order and restaurant screens); console 163 (51 new: the moderation and reply actions, the buttons, the helpers). Typecheck, lint and prettier clean.
- **Real run** against the API and PostgreSQL: a customer on the Android emulator opened a delivered order, wrote a five-star review with a phone number in it, and saw it waiting;
  in the browser a moderator saw it flagged "Contains a phone number" and published it, and the business owner replied; the restaurant's page in the app then showed the review with
  the business's reply under it.
- **Not in 10.2, and known gaps:** a list of "my reviews" (a review is found on its own booking or order); paging through more than 25 reviews; a marker on the history list for orders
  and bookings that can be reviewed; telling the author or the business when something happens to a review (the notifications gap); photos. A moderator can still publish a review the
  automatic check flagged: the flag is information, and the decision is theirs.

## 8. As built: step 10.3

Flow in [workflows.md](workflows.md) §27. The decisions R9 to R11 in section 1 were built as the proposed defaults; the owner has not yet named the grievance officer or confirmed the targets.

- **Who can ask, and about what (R9).** A signed-in customer, or a business owner about one of their businesses, writes a request with a topic (a booking, a food order, a payment or refund,
  an account, a business, something else), a subject and a message, and may tie it to **one** booking or order of their own (or their business's): support then sees that one thing, not their
  history. Starting a request needs the `support` module to be switched on (it starts off, and the customer app keeps it at "coming soon" until then); reading and answering existing ones does not.
  Each person can have five unresolved requests at once, and a thread is limited to 100 messages, so one person cannot bury the queue.
- **The conversation.** The person and support write in turn. Support's messages reach the person as "OnePondy support", with no staff member's name. A request is **open** (waiting for OnePondy),
  **waiting for the person** (support has answered) or **resolved**; the person can reopen a resolved request by replying for 14 days (`Support:ReopenWindow`), after which they start a new one.
  Deleting an account deletes the person's requests and messages.
- **The agents' queue (R9, R10).** Administrators and support agents (not content editors: the new `support` policy) see lists for needs a reply, waiting, mine, unassigned, escalated, overdue and
  resolved, with counts. An agent sees the request, the first name on the account (a name that is really a number or an e-mail address shows as "A customer"), and the one booking or order it is about;
  never the person's other bookings, orders, phone number or address. **Opening a request is audited each time**, and the person's phone number and e-mail address are a separate step ("Show contact
  details") that is audited too. Taking (assigning), releasing, resolving and escalating a request are audited. Every change carries the version the agent was looking at, so a request that moved on is refused.
- **Targets and escalation (R9).** A request with no first answer within `Support:FirstReplyTarget` (24 hours), or still unresolved after `Support:ResolutionTarget` (5 days), is flagged in the queue, and the Worker's
  `SupportEscalationJob` (every five minutes) escalates it once with the reason in the audit log. An agent can also escalate with their own reason. Escalated requests sort first, and an administrator sees them
  in the "Escalated" list; escalation changes nobody's permissions.
- **The grievance officer (R11).** `GET /support/grievance` is public: the customer app's Help screen and a public web page (`/grievance` in the console) show the name and the contact details from the settings
  `Support:GrievanceOfficer`, with the 48-hour and one-month promises (`AcknowledgeWithinHours`, `ResolveWithinDays`). Until an officer is appointed both say so plainly. The Help screen always shows it,
  even to a guest and while requests are not open.
- **API.** Customers and business owners: `POST /support/tickets`, `GET /support/tickets`, `GET /support/tickets/{id}`, `POST /support/tickets/{id}/messages`. Agents: `GET /admin/support/tickets?view=`,
  `GET /admin/support/tickets/{id}`, `GET .../contact`, `POST .../messages`, `.../assign`, `.../release`, `.../resolve`, `.../escalate`, `GET /admin/support/counts`.
- **Customer app.** Profile's "Help and support" opens a Help screen (the person's requests with where each stands, "New request", the grievance officer). "Get help with this booking" / "this order"
  on those screens starts a request about it. A request is a thread with the person's and support's messages, the linked booking or order in a card, and a reply box; it refreshes every 30 seconds and when
  the screen is shown. A request resolved too long ago says to start a new one.
- **Console.** A **Support** page for administrators and support agents (menu link; lists with counts; flags for escalated, no first reply in time and open too long), a request page (conversation,
  the linked booking or order, an answer box, Take it / Release / Resolve / Escalate with a reason, and "Show contact details" with a note that asking is recorded), and, for business owners, a **Support**
  page in the partner area (their requests, a form for a new one about a chosen business, and the thread).
- **Database.** Migration `AddSupportTickets`: `support_tickets` (checks for status, topic, subject length, about-a-booking-or-an-order-not-both, resolved-means-a-time-and-a-person, assigned and escalated
  columns agreeing) and `support_messages` (cascade with the request). No triggers.
- **Tests.** Server: 43 new unit tests (the request's own rules, the service for requesters and agents, the audit entries, the targets, the Worker's escalation, deletion with the account) and six against
  PostgreSQL (the full HTTP flow with real roles, the business owner's request, every agent list on PostgreSQL, the Worker's escalation of an old request, the public grievance page, the database's checks).
  Customer app 428 (38 new). Console 224 (61 new).
- **Found in the real run and fixed:** the customer app kept `support` at "coming soon" even when the server said available, because the module was not on the app's list of what it can open. It is now.
- **Not in 10.3, and known gaps:** a push notification when support answers (notifications are about bookings only; the customer sees the answer when they open the request, which refreshes itself, or
  from Help); attachments or photos; internal notes between agents; assigning to a named colleague (an agent takes a request for themselves); a business owner linking a booking or order from the console
  (the API supports it); a customer rating the answer; and the legal review of the grievance officer's wording and the 48-hour and one-month promises.

## 9. As built: step 10.4

Details in [backend-architecture.md](backend-architecture.md) §5.4. Decision R12 was built as proposed.

- **Every API request is limited,** not only sign-in: a classifier puts each in `browse`, `read`, `write`, `create`, `upload` or `staff` (sign-in and account endpoints keep their own per-address limit; health checks, files and the
  payment gateway's callbacks are exempt). Each class has a setting for its limit and window. Defaults: 120, 240, 90, 12, 20 and 600 requests a minute.
- **Per person, not per address.** A signed-in person is counted on their own, and anyone else by address, so one person cannot use up another's allowance and a shared address does not lock out the people behind it. The
  limiter now runs after authentication for this; a bad token is limited as the public read it is.
- **Refusals** are `429` with `code: "rate_limited"` and `Retry-After`, are logged as warnings, and are counted per class (counts only: no address, no account) for the operations page in 10.5.
- **Tests.** Classifier table (what each endpoint is, and what is exempt, including paths that only look like an exempt one), the limits over HTTP (refuse, say when to come back, work again after the window, leave health,
  gateway callbacks and sign-in alone, count correctly), and against PostgreSQL that one person's limit does not touch another's from the same address. The shared test host sets the new limits very high so other tests are unaffected.
- **Not in 10.4:** limits that adapt to load, blocking an address for longer after repeated refusals, per-endpoint limits finer than the classes, and a shared store so several API servers count together (the counts live in one
  process; with more than one API server each has its own).

## 10. As built: step 10.5

Details in [backend-architecture.md](backend-architecture.md) §7.14. Decision R13 was built as proposed: alerts are shown, not delivered.

- **Operations page** (console, administrators only; `GET /api/v1/admin/operations`). It adds no new data. It counts what the platform already keeps: reviews waiting for a moderator (and how long the oldest has waited), support
  requests needing a reply, overdue and escalated, food orders that look stuck, open payment differences, notifications not yet sent, sign-in codes asked for in the last hour and day, and the rate limiter's counts per class.
- **Alerts** turn those numbers and the thresholds in the `Operations` settings into a short list, most serious first. Each says what is wrong in a sentence and links to the page that deals with it. Payment
  differences and escalated support requests are *act now*; the rest are *look today*. Defaults: a review waiting more than 2 days, a notification waiting more than 15 minutes (only while sending is switched on), more
  than 200 sign-in codes in an hour, 50 refused requests in one class.
- **Audit log filters:** reviews, support requests, food orders, restaurants, drivers, driver cash and statements, with plain wording for every action those record, and links from a support request or an order entry
  to the item.
- **Tests.** The alert rules as a pure table (each rule fires, stays quiet below its threshold, and the order is by seriousness), the snapshot over an in-memory database (counts, oldest waiting, the notification setting),
  and against PostgreSQL that only administrators may read it and that every part of it is answered. Console: the alert links and the age wording.
- **Not in 10.5:** sending an alert by e-mail, chat or pager (needs a provider and the owner's choice), history or graphs (the page is a snapshot; reload to refresh), and limiter counts that survive a restart or span
  several API servers.
