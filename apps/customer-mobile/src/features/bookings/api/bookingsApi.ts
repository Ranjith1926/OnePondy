import { apiRequest } from '../../../api/apiClient';
import type { CancellationPolicy, ListingType } from '../../listings/api/listingsApi';

// Shapes returned by GET /api/v1/bookings (spec §7, §10, Phase 6). Enums arrive in camelCase.

export type BookingStatus =
  | 'pending'
  | 'awaitingPayment'
  | 'confirmed'
  | 'failed'
  | 'expired'
  | 'rejected'
  | 'cancelled'
  | 'refundPending'
  | 'refunded'
  | 'completed';

export type BookingActorType = 'customer' | 'partner' | 'admin' | 'system';

export interface PriceSnapshot {
  unitPrice: number;
  quantity: number;
  subtotal: number;
  fees: number;
  taxAmount: number;
  discountAmount: number;
  totalAmount: number;
  currency: string;
}

export interface BookingSummary {
  id: string;
  listingId: string;
  listingTitle: string;
  businessId: string;
  businessName: string;
  bookingType: ListingType;
  status: BookingStatus;
  expiresAt: string | null;
  startAt: string | null;
  endAt: string | null;
  guestCount: number;
  totalAmount: number;
  currency: string;
  createdAt: string;
}

export interface BookingSlotView {
  slotId: string;
  startAt: string;
  endAt: string;
  quantity: number;
}

export interface BookingHistoryView {
  previousStatus: BookingStatus | null;
  newStatus: BookingStatus;
  actorType: BookingActorType;
  reason: string | null;
  createdAt: string;
}

export interface BookingDetail {
  id: string;
  version: number;
  listingId: string;
  listingTitle: string;
  businessId: string;
  businessName: string;
  bookingType: ListingType;
  status: BookingStatus;
  expiresAt: string | null;
  startAt: string | null;
  endAt: string | null;
  guestCount: number;
  priceSnapshot: PriceSnapshot;
  cancellationPolicySnapshot: CancellationPolicy;
  cancellationReason: string | null;
  slots: BookingSlotView[];
  history: BookingHistoryView[];
  createdAt: string;
}

export interface CreateBookingInput {
  listingId: string;
  slotIds: string[];
  guestCount: number;
  idempotencyKey: string;
}

export const bookingsApi = {
  create: (input: CreateBookingInput) =>
    apiRequest<BookingDetail>('/api/v1/bookings', { method: 'POST', body: input, auth: true }),

  list: () => apiRequest<BookingSummary[]>('/api/v1/bookings', { auth: true }),

  get: (id: string) =>
    apiRequest<BookingDetail>(`/api/v1/bookings/${encodeURIComponent(id)}`, { auth: true }),

  cancel: (id: string, version: number, reason?: string) =>
    apiRequest<BookingDetail>(`/api/v1/bookings/${encodeURIComponent(id)}/cancel`, {
      method: 'POST',
      body: { version, reason },
      auth: true,
    }),
};
