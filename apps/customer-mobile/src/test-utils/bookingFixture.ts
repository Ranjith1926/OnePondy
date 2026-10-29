import type { BookingDetail } from '../features/bookings/api/bookingsApi';

export const bookingId = '3f0c2f64-7a5b-4a52-9d58-0d1d4f2d9b11';

/** A booking waiting for payment, with every field filled; tests override what they care about. */
export function aBooking(overrides: Partial<BookingDetail> = {}): BookingDetail {
  return {
    id: bookingId,
    version: 3,
    listingId: 'listing-1',
    listingTitle: 'Sunrise boat ride',
    businessId: 'business-1',
    businessName: 'Lakeside Tours',
    bookingType: 'slot',
    status: 'awaitingPayment',
    expiresAt: '2026-10-29T10:30:00Z',
    startAt: '2026-11-02T06:00:00Z',
    endAt: '2026-11-02T07:00:00Z',
    guestCount: 2,
    priceSnapshot: {
      unitPrice: 500,
      quantity: 2,
      subtotal: 1000,
      fees: 0,
      taxAmount: 0,
      discountAmount: 0,
      totalAmount: 1000,
      currency: 'INR',
    },
    cancellationPolicySnapshot: { freeCancellationHours: 24, refundPercentage: 50 },
    cancellationReason: null,
    slots: [],
    history: [],
    createdAt: '2026-10-29T10:00:00Z',
    updatedAt: '2026-10-29T10:05:00Z',
    payment: null,
    refunds: [],
    ...overrides,
  } as BookingDetail;
}
