/** The kinds of notification the server sends (spec Phase 7). Each carries only its kind and a booking id. */
const knownTypes = new Set([
  'booking.requested',
  'booking.accepted',
  'booking.confirmed',
  'booking.paid',
  'booking.rejected',
  'booking.cancelled',
  'booking.expired',
  'booking.reminder',
  'refund.processed',
]);

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export interface BookingLink {
  bookingId: string;
  type: string;
}

/**
 * Reads where tapping a notification should go. The data comes from outside the app, so it is checked: anything that is not
 * a known kind with a well-formed booking id is ignored. Opening the booking still goes through the signed-in API, which
 * refuses anyone who is not its customer, so a forged link reveals nothing.
 */
export function bookingLinkFrom(data: unknown): BookingLink | null {
  if (typeof data !== 'object' || data === null) {
    return null;
  }

  const { bookingId, type } = data as { bookingId?: unknown; type?: unknown };
  if (
    typeof bookingId !== 'string' ||
    !uuidPattern.test(bookingId) ||
    typeof type !== 'string' ||
    !knownTypes.has(type)
  ) {
    return null;
  }

  return { bookingId, type };
}
