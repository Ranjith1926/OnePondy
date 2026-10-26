# Data retention: sign-in and account data

Status: Phase 2 draft. It covers only the data that sign-in and accounts create. Booking, payment
and review data are added when those phases arrive. Retention periods for financial records need
the compliance review in spec §21 and are not set here.

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
2. Refuses while bookings, orders or refunds are in progress, and lists what to resolve first. Until those features exist, nothing blocks deletion.
3. Revokes every session.
4. Anonymises the account: the name becomes "Deleted user", the phone number and email are removed, and the security stamp is renewed so old tokens stop working.
5. Sends a confirmation to the contact that was used.

The public page never reveals whether an account exists for a phone number or email.

## Logs

Production never writes one-time codes to logs. Only a test or development server may do so
(`Auth:Otp:Delivery = Log`), and the API refuses to start with that setting in any other
environment.
