import { isApiError, isNetworkError } from '../../api/apiErrors';

const knownCodes = [
  'booking_changed',
  'booking_invalid_transition',
  'booking_not_found',
  'refunds_not_enabled',
  'payment_not_due',
  'payment_window_closed',
  'payments_unavailable',
  'not_enough_availability',
] as const;

type KnownCode = (typeof knownCodes)[number];

export type BookingErrorMessageKey =
  `bookings.errors.${KnownCode | 'offline' | 'timeout' | 'generic'}`;

function isKnownCode(code: string): code is KnownCode {
  return (knownCodes as readonly string[]).includes(code);
}

/** Maps a booking or payment failure to a translatable message, so the customer is told what happened and what to do. */
export function bookingErrorMessageKey(error: unknown): BookingErrorMessageKey {
  if (isNetworkError(error)) {
    return error.reason === 'timeout' ? 'bookings.errors.timeout' : 'bookings.errors.offline';
  }

  if (isApiError(error) && isKnownCode(error.code)) {
    return `bookings.errors.${error.code}`;
  }

  return 'bookings.errors.generic';
}
