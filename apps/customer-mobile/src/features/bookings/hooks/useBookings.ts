import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { bookingsApi, type CreateBookingInput } from '../api/bookingsApi';

export const bookingsQueryKey = ['bookings'] as const;
export const bookingQueryKey = (id: string) => ['booking', id] as const;

export function useBookings() {
  return useQuery({
    queryKey: bookingsQueryKey,
    queryFn: () => bookingsApi.list(),
  });
}

export function useBooking(id: string | undefined) {
  return useQuery({
    queryKey: bookingQueryKey(id ?? ''),
    queryFn: () => bookingsApi.get(id!),
    enabled: id !== undefined,
  });
}

export function useCreateBooking() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateBookingInput) => bookingsApi.create(input),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: bookingsQueryKey }),
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
