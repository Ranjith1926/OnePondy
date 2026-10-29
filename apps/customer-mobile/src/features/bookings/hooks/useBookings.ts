import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { askForPushAfterBooking } from '../../notifications/hooks/usePushSetup';
import { bookingsApi, type CreateBookingInput } from '../api/bookingsApi';

export const bookingsQueryKey = ['bookings'] as const;
export const bookingQueryKey = (id: string) => ['booking', id] as const;

export function useBookings() {
  return useQuery({
    queryKey: bookingsQueryKey,
    queryFn: () => bookingsApi.list(),
  });
}

/** While a booking waits for payment or a refund, the screen re-reads it so it updates by itself (the webhook may land at any moment). */
export function bookingRefetchInterval(status: string | undefined): number | false {
  if (status === 'awaitingPayment') {
    return 5_000;
  }

  return status === 'refundPending' ? 15_000 : false;
}

export function useBooking(id: string | undefined) {
  return useQuery({
    queryKey: bookingQueryKey(id ?? ''),
    queryFn: () => bookingsApi.get(id!),
    enabled: id !== undefined,
    refetchInterval: (query) => bookingRefetchInterval(query.state.data?.status),
  });
}

export function useCreateBooking() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateBookingInput) => bookingsApi.create(input),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: bookingsQueryKey });
      // The first booking is when a notification (accepted, confirmed) becomes worth having, so this is when we ask, once.
      void askForPushAfterBooking();
    },
  });
}

export function useCancelBooking(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ version, reason }: { version: number; reason?: string }) =>
      bookingsApi.cancel(id, version, reason),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: bookingsQueryKey });
      void queryClient.invalidateQueries({ queryKey: bookingQueryKey(id) });
    },
  });
}
