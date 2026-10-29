import { apiRequest } from '../../../api/apiClient';
import type { BookingDetail } from '../../bookings/api/bookingsApi';

/** What the app needs to open the gateway's checkout. The key id is public; the key secret never leaves the server. */
export interface PaymentStart {
  bookingId: string;
  attemptId: string;
  provider: 'razorpay';
  /** `sandbox` means test money: nothing real is charged. */
  mode: 'sandbox' | 'live';
  keyId: string;
  orderId: string;
  /** Rupees, for showing to the customer. */
  amount: number;
  /** Whole paise, for the gateway. */
  amountPaise: number;
  currency: string;
  paymentExpiresAt: string;
}

export const paymentsApi = {
  /** Creates (or returns the open) gateway order for a booking the partner has accepted. Safe to repeat. */
  start: (bookingId: string) =>
    apiRequest<PaymentStart>(`/api/v1/bookings/${encodeURIComponent(bookingId)}/payment`, {
      method: 'POST',
      auth: true,
    }),

  /**
   * "Is it paid yet?" The server asks the gateway itself and applies a captured payment, then returns the booking. The
   * app never decides whether a payment succeeded: only the server's answer counts.
   */
  refresh: (bookingId: string) =>
    apiRequest<BookingDetail>(`/api/v1/bookings/${encodeURIComponent(bookingId)}/payment/refresh`, {
      method: 'POST',
      auth: true,
    }),
};
