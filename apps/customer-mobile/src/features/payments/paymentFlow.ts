import { ApiError } from '../../api/apiErrors';
import type { BookingDetail } from '../bookings/api/bookingsApi';
import type { CheckoutLauncher } from './checkout/razorpayCheckout';

/** What opening the gateway's checkout needs, whether it is for a booking or a food order. */
export interface GatewayStart {
  keyId: string;
  orderId: string;
  amountPaise: number;
  currency: string;
  paymentExpiresAt: string;
}

/** Anything that waits for payment and then is confirmed: a booking or a food order. Both use the same status names. */
export interface Payable {
  status: string;
}

/**
 * How paying ended. Every outcome except `error` comes from asking the server what really happened, never
 * from the checkout's own report (spec Phase 7: "a success reported by the app is never trusted").
 */
export type PayOutcome<T extends Payable = BookingDetail> =
  /** The server confirms it is paid and confirmed. */
  | { kind: 'confirmed'; booking: T }
  /** The customer paid but the server has not confirmed it yet; it will, and they are notified. */
  | { kind: 'processing'; booking: T | null }
  /** The customer closed the checkout without paying. They can try again while time remains. */
  | { kind: 'cancelled'; booking: T | null }
  /** The payment did not go through. They can try again while time remains. */
  | { kind: 'failed'; booking: T | null }
  /** A payment was made but it had already lapsed: the money is being refunded. */
  | { kind: 'lapsed'; booking: T }
  /** Paying could not even start (no connection, the time to pay ran out, payments unavailable). */
  | { kind: 'error'; error: unknown };

export interface PayDependencies<T extends Payable = BookingDetail> {
  start: (id: string) => Promise<GatewayStart>;
  checkout: CheckoutLauncher;
  /** Asks the server to check the gateway and returns the booking or order as it now stands. */
  refresh: (id: string) => Promise<T>;
  delay: (milliseconds: number) => Promise<void>;
  now: () => number;
}

export interface PayOptions {
  /** How long to wait between asking the server whether it has seen the payment. */
  pollIntervalMs?: number;
  /** How many times to ask after the checkout reports a payment. */
  maxPolls?: number;
}

const lapsedStatuses = new Set<string>([
  'expired',
  'cancelled',
  'rejected',
  'refundPending',
  'refunded',
  'failed',
]);

/**
 * Pays for something the partner has accepted: asks the server for a gateway order, opens the checkout, then asks the server
 * what happened. After a reported payment it keeps asking for a short while, because the server may need a moment to see it.
 */
export async function payFor<T extends Payable>(
  id: string,
  description: string,
  deps: PayDependencies<T>,
  { pollIntervalMs = 2_000, maxPolls = 6 }: PayOptions = {},
): Promise<PayOutcome<T>> {
  let started: GatewayStart;
  try {
    started = await deps.start(id);
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
  let booking: T | null = null;
  try {
    booking = await deps.refresh(id);
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
      booking = await deps.refresh(id);
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

/** Pays for a booking the partner has accepted. */
export function payForBooking(
  bookingId: string,
  description: string,
  deps: PayDependencies,
  options?: PayOptions,
): Promise<PayOutcome> {
  return payFor<BookingDetail>(bookingId, description, deps, options);
}
