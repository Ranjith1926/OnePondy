import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';

import { useAppState } from '../../../state';
import { ridesApi, type IncidentCategory, type PlaceChoice } from '../api/ridesApi';
import { refetchInterval } from '../rideLogic';
import { useRideLive } from './useRideLive';

export const currentRideKey = ['rides', 'current'] as const;

/**
 * The rider's current ride, kept fresh: pushed over the live connection when it is up, polled when it is not. Signed-out people ask nothing.
 */
export function useCurrentRide() {
  const { auth } = useAppState();
  const queryClient = useQueryClient();
  const signedIn = auth.status === 'authenticated';
  const refresh = useCallback(
    () => void queryClient.invalidateQueries({ queryKey: currentRideKey }),
    [queryClient],
  );
  const live = useRideLive(signedIn, refresh);
  const query = useQuery({
    queryKey: currentRideKey,
    queryFn: () => ridesApi.current(),
    enabled: signedIn,
    // Read from the data, so polling stops by itself when the ride ends.
    refetchInterval: (state) => refetchInterval(state.state.data, live.connected),
  });

  return { ...query, connected: live.connected };
}

export function useQuotes() {
  return useMutation({
    mutationFn: ({ pickup, destination }: { pickup: PlaceChoice; destination: PlaceChoice }) =>
      ridesApi.quote(pickup, destination),
  });
}

export function useRequestRide() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ quoteId, note }: { quoteId: string; note?: string }) =>
      ridesApi.request(quoteId, note),
    onSuccess: (ride) => queryClient.setQueryData(currentRideKey, ride),
  });
}

export function useCancelRide() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => ridesApi.cancel(id),
    onSuccess: (ride) => queryClient.setQueryData(currentRideKey, ride),
  });
}

export function useSos() {
  return useMutation({
    mutationFn: ({
      id,
      position,
    }: {
      id: string;
      position: { latitude: number; longitude: number } | null;
    }) => ridesApi.sos(id, position),
  });
}

export function useReportIncident() {
  return useMutation({
    mutationFn: ({
      id,
      category,
      details,
    }: {
      id: string;
      category: IncidentCategory;
      details: string;
    }) => ridesApi.incident(id, category, details),
  });
}

export function useShareRide() {
  return useMutation({ mutationFn: (id: string) => ridesApi.share(id) });
}
