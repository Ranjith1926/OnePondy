import { useMutation, useQueryClient } from '@tanstack/react-query';

import { bookingQueryKey, bookingsQueryKey } from '../../bookings/hooks/useBookings';
import { paymentsApi } from '../api/paymentsApi';
import { openRazorpayCheckout } from '../checkout/razorpayCheckout';
import { payForBooking, type PayDependencies } from '../paymentFlow';

const realDependencies: PayDependencies = {
  start: paymentsApi.start,
  checkout: openRazorpayCheckout,
  refresh: paymentsApi.refresh,
  delay: (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)),
  now: () => Date.now(),
};

/**
 * Pays for a booking the partner has accepted. The outcome, in `data`, always comes from asking the server; the booking
 * on screen is refreshed afterwards so it shows what really happened. `dependencies` is for tests.
 */
export function usePayBooking(
  bookingId: string,
  description: string,
  dependencies: PayDependencies = realDependencies,
) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => payForBooking(bookingId, description, dependencies),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: bookingQueryKey(bookingId) });
      void queryClient.invalidateQueries({ queryKey: bookingsQueryKey });
    },
  });
}
