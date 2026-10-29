# Data retention: sign-in, account, content, partner and booking data

Status: Phase 2 to 7 draft. It covers the data that sign-in, accounts, content management,
partners, bookings, payments and notifications create. Review data is added when that phase arrives.
Retention periods for financial records, partner documents and bookings need the compliance review
in spec §21 and are not set here: where this page says "kept", no end date has been decided yet.

## Sign-in data

| Data | What is stored | Kept for | Removed by |
|---|---|---|---|
| One-time code challenges | Destination (phone or email), channel, purpose, a keyed hash of the code, attempt count, timestamps. The code itself is never stored. | 1 day after creation | Worker, hourly |
| Refresh tokens | A keyed hash of the token, its session, expiry and consumption time. Used tokens are kept so reuse can be detected. | 30 days after expiry or use | Worker, hourly |
| Sessions | Device name, creation, last use, expiry and revocation reason. | 90 days after expiry or revocation | Worker, hourly |

Deleting a session also deletes its remaining refresh tokens.

## Account data

| Data | Kept for | Notes |
|---|---|---|
| Name, mobile number, email, preferred language | While the account exists | Removed when the account is deleted. |
| Account row | Indefinitely, anonymised after deletion | Records that must outlive the account (invoices, payments) stay linked to this row. |

## Account deletion

Deleting an account (in the app, or on the public page at `/account/delete/`) does the following:

1. Re-confirms with a one-time code sent to the account's own phone or email.
2. Refuses while a booking is in progress (pending, awaiting payment, confirmed or refund pending), both for the customer who made it and for the owner of the business it is with, and says what to resolve first. Orders and rides add their own checks in Phases 8 and 9. In the app the user is told this when they ask for the code, so no code is sent for a deletion that cannot go ahead; the confirm step checks again in case a booking was made in between.
3. Revokes every session.
4. Anonymises the account: the name becomes "Deleted user", the phone number and email are removed, and the security stamp is renewed so old tokens stop working.
5. Removes the account's saved places, saved listings and trip plans.
6. Sends a confirmation to the contact that was used.

The public page never reveals whether an account exists for a phone number or email, so it checks for blocking bookings only after the code is verified.

## Content and configuration (Phase 3)

| Data | Kept for | Notes |
|---|---|---|
| Places, translations, entry fees | Indefinitely | Never hard-deleted; unpublish instead (spec §9). A place taken off the app for good is unpublished and left as a draft, not removed. |
| Place photos | Indefinitely, until a staff member removes them | Stored on local disk for now (spec D8); the file and its three sizes are deleted together with the photo's record. |
| Audit log | Indefinitely | Append-only: the database rejects any `UPDATE`, `DELETE` or `TRUNCATE` on `audit_log` (enforced by a trigger, not just application code). Entries record the staff member's user id, which is kept even if that person's access is later removed, so the trail of who changed what stays intact. |
| Staff accounts | While they have a role | Removing someone's access revokes their sessions at once (`SessionRevocationReason.RoleChanged`) but does not delete their underlying account; they keep a customer account like anyone else. |

## Partners, listings and bookings (Phase 6)

| Data | Kept for | Notes |
|---|---|---|
| Business applications | Indefinitely | Never hard-deleted. An owner's account may be anonymised while the business stays; a business with bookings in progress blocks that deletion. |
| GSTIN and licence documents | While the business exists; no end date decided | Stored in a private folder (`BusinessDocuments:RootPath`, default `App_Data/business-documents`) that the API never serves as static files. Only the owner and administrators can download them, through the API, and every download needs a session. The retention period needs the §21 review. |
| Listings and availability slots | Indefinitely | A listing is unpublished or suspended, not deleted. A slot with bookings against it cannot be removed. |
| Bookings | Linked to the anonymised customer row; no end date decided | Never deleted with the account: the account row is kept, which is why bookings do not cascade from it (spec §17.3). Includes the price and cancellation terms the customer saw. |
| Booking status history | As long as the booking | Append-only. The database rejects `UPDATE`, `DELETE` and `TRUNCATE` on it with a trigger, and its link to the booking is `RESTRICT`, so history is never cascade-deleted. Records who or what made each change (customer, partner, administrator or the system) by user id. |
| Saved listings | Until the customer removes them or deletes their account | Removed with the account. |
| Held inventory | Until the hold's `expires_at`, 30 minutes after the request | A booking nobody acts on is expired by the Worker every minute and its places go back on sale. **The Worker must be running** for this: without it, a pending booking holds its places indefinitely. |

## Payments and notifications (Phase 7)

| Data | Kept for | Notes |
|---|---|---|
| Payment attempts, refunds, ledger entries, settlements | No end date decided | Financial records, linked to the anonymised customer row like bookings. The ledger is append-only (a trigger rejects `UPDATE`, `DELETE` and `TRUNCATE`). The retention period needs the §21 tax review. |
| Gateway webhook events | No end date decided | Stored with only ids, amounts and statuses: never the payer's email, phone number, card or UPI details. |
| Partner statements and their CSV | No end date decided | A statement lists booking ids, listing titles and amounts only: no customer name, phone or email. Generated on request, not stored as files. Same retention question as the settlements above. |
| Reconciliation exceptions | No end date decided | Differences between our records and the gateway's, with the administrator's resolution note. Never deleted by the application. |
| Device tokens (push) | Until the session ends, the phone signs out, the phone loses the app, or the account is deleted | Deleted with their session or user (database cascade), by the sender when Expo says the phone is gone, and explicitly on account deletion. |
| Notification log | 30 days | Type, status and Expo's ticket id. Never the message text. Removed by the Worker. |
| Outbox messages | 30 days after they are finished | Holds only who the notification is for and which booking; removed by the Worker once no log row still refers to it. A message not sent within 24 hours is dropped, not delivered late. |

## Logs

Production never writes one-time codes to logs. Only a test or development server may do so
(`Auth:Otp:Delivery = Log`), and the API refuses to start with that setting in any other
environment.
