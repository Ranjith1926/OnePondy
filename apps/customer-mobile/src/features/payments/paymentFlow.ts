import { ApiError } from '../../api/apiErrors';
import type { BookingDetail } from '../bookings/api/bookingsApi';
import type { PaymentStart } from './api/paymentsApi';
import type { CheckoutLauncher } from './checkout/razorpayCheckout';

/**
 * How paying for a booking ended. Every outcome except `error` comes from asking the server what really happened, never
 * from the checkout's own report (spec Phase 7: "a success reported by the app is never trusted").
 */
export type PayOutcome =
  /** The server confirms the booking is paid and confirmed. */
  | { kind: 'confirmed'; booking: BookingDetail }
  /** The customer paid but the server has not confirmed it yet; it will, and they are notified. */
  | { kind: 'processing'; booking: BookingDetail | null }
  /** The customer closed the checkout without paying. They can try again while time remains. */
  | { kind: 'cancelled'; booking: BookingDetail | null }
  /** The payment did not go through. They can try again while time remains. */
  | { kind: 'failed'; booking: BookingDetail | null }
  /** A payment was made but the booking had already lapsed: the money is being refunded. */
  | { kind: 'lapsed'; booking: BookingDetail }
  /** Paying could not even start (no connection, the time to pay ran out, payments unavailable). */
  | { kind: 'error'; error: unknown };

export interface PayDependencies {
  start: (bookingId: string) => Promise<PaymentStart>;
  checkout: CheckoutLauncher;
  /** Asks the server to check the gateway and returns the booking as it now stands. */
  refresh: (bookingId: string) => Promise<BookingDetail>;
  delay: (milliseconds: number) => Promise<void>;
  now: () => number;
}

export interface PayOptions {
  /** How long to wait between asking the server whether it has seen the payment. */
  pollIntervalMs?: number;
  /** How many times to ask after the checkout reports a payment. */
  maxPolls?: number;
}

const lapsedStatuses = new Set<BookingDetail['status']>([
  'expired',
  'cancelled',
  'rejected',
  'refundPending',
  'refunded',
  'failed',
]);

/**
 * Pays for a booking the partner has accepted: asks the server for a gateway order, opens the checkout, then asks the server
 * what happened. After a reported payment it keeps asking for a short while, because the server may need a moment to see it.
 */
export async function payForBooking(
  bookingId: string,
  description: string,
  deps: PayDependencies,
  { pollIntervalMs = 2_000, maxPolls = 6 }: PayOptions = {},
): Promise<PayOutcome> {
  let started: PaymentStart;
  try {
    started = await deps.start(bookingId);
  } catch (error) {
    return { kind: 'error', error };
  }

  const secondsLeft = Math.floor((Date.parse(started.paymentExpiresAt) - deps.now()) / 1_000);
  if (secondsLeft <= 0) {
    return { kind: 'error', error: new ApiError(409, 'payment_window_closed') };
  }

  const result = await deps.checkout({
    keyId: started.keyId,
    orderId: started.orderId,
    amountPaise: started.amountPaise,
    currency: started.currency,
    description,
    timeoutSeconds: secondsLeft,
  });

  // Whatever the checkout said, ask the server what actually happened.
  let booking: BookingDetail | null = null;
  try {
    booking = await deps.refresh(bookingId);
  } catch {
    // Offline just now. If a payment was reported, it will still be applied; the customer is told it is being confirmed.
  }

  if (booking?.status === 'confirmed') {
    return { kind: 'confirmed', booking };
  }

  if (result.kind !== 'completed') {
    return { kind: result.kind, booking };
  }

  for (let poll = 0; poll < maxPolls; poll++) {
    if (booking && booking.status !== 'awaitingPayment') {
      break;
    }

    await deps.delay(pollIntervalMs);
    try {
      booking = await deps.refresh(bookingId);
    } catch {
      continue;
    }

    if (booking.status === 'confirmed') {
      return { kind: 'confirmed', booking };
    }
  }

  if (booking && lapsedStatuses.has(booking.status)) {
    return { kind: 'lapsed', booking };
  }

  return { kind: 'processing', booking };
}
