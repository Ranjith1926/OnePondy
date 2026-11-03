import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import * as Location from 'expo-location';
import { useCallback, useEffect, useState } from 'react';

import { isApiError } from '../../../api/apiErrors';
import { useSession } from '../../../session/SessionProvider';
import { rideDriverApi, type IncidentCategory, type RideDriverDetails } from '../api/rideDriverApi';
import { useRideLive } from './useRideLive';

export const rideKeys = {
  me: ['rideDriver', 'me'] as const,
  rides: ['rideDriver', 'rides'] as const,
};

/** The signed-in person's ride application and duty. Disabled while signed out. */
export function useRideDriverProfile() {
  const { status } = useSession();
  return useQuery({
    queryKey: rideKeys.me,
    queryFn: rideDriverApi.getMine,
    enabled: status === 'signedIn',
  });
}

function useReloadOnConflict() {
  const queryClient = useQueryClient();
  return (error: unknown) => {
    if (isApiError(error) && error.code === 'ride_driver_changed') {
      void queryClient.invalidateQueries({ queryKey: rideKeys.me });
    }
  };
}

export function useSaveRideDriver() {
  const queryClient = useQueryClient();
  const reload = useReloadOnConflict();
  return useMutation({
    mutationFn: (details: RideDriverDetails) => rideDriverApi.save(details),
    onSuccess: (profile) => queryClient.setQueryData(rideKeys.me, profile),
    onError: reload,
  });
}

export function useSetRideDuty() {
  const queryClient = useQueryClient();
  const reload = useReloadOnConflict();
  return useMutation({
    mutationFn: ({ onDuty, version }: { onDuty: boolean; version: number }) =>
      rideDriverApi.setDuty(onDuty, version),
    onSuccess: (profile) => queryClient.setQueryData(rideKeys.me, profile),
    onError: reload,
  });
}

/**
 * The ride the driver holds and, while free and on duty, the requests they can take: pushed over the live connection when it is up, polled
 * quickly when it is not, and slowly as a safety net when it is.
 */
export function useRides(enabled: boolean) {
  const queryClient = useQueryClient();
  const refresh = useCallback(
    () => void queryClient.invalidateQueries({ queryKey: rideKeys.rides }),
    [queryClient],
  );
  const live = useRideLive(enabled, refresh);
  const query = useQuery({
    queryKey: rideKeys.rides,
    queryFn: rideDriverApi.rides,
    enabled,
    refetchInterval: live.connected ? 60_000 : 10_000,
  });
  return { ...query, connected: live.connected };
}

/** One step on a ride. Whatever happens, the list is reloaded, so a driver who lost a race sees what is true now. */
function useRideStep<TVariables>(step: (variables: TVariables) => Promise<unknown>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: step,
    onSettled: () => queryClient.invalidateQueries({ queryKey: rideKeys.rides }),
  });
}

export const useAcceptRide = () => useRideStep((id: string) => rideDriverApi.accept(id));
export const useGiveUpRide = () => useRideStep((id: string) => rideDriverApi.giveUp(id));
export const useArriveRide = () => useRideStep((id: string) => rideDriverApi.arrive(id));
export const useStartRide = () => useRideStep((id: string) => rideDriverApi.start(id));
export const useCompleteRide = () =>
  useRideStep(({ id, cash }: { id: string; cash: number }) => rideDriverApi.complete(id, cash));
export const useCancelRideAsDriver = () =>
  useRideStep(({ id, reason }: { id: string; reason: string }) => rideDriverApi.cancel(id, reason));

export function useRideSos() {
  return useMutation({
    mutationFn: ({
      id,
      position,
    }: {
      id: string;
      position: { latitude: number; longitude: number } | null;
    }) => rideDriverApi.sos(id, position),
  });
}

export function useRideIncident() {
  return useMutation({
    mutationFn: ({
      id,
      category,
      details,
    }: {
      id: string;
      category: IncidentCategory;
      details: string;
    }) => rideDriverApi.incident(id, category, details),
  });
}

const LOCATION_INTERVAL_MS = 15_000;
const LOCATION_DISTANCE_M = 25;

/**
 * Reports where the driver is for as long as they are on duty for rides, and not a moment longer. The position goes to dispatch (so requests near
 * the driver are offered to them) and, while they hold a ride, onto that ride for the rider to follow. Only foreground location is used, so
 * nothing is reported while the app is closed. If the driver says no to location, being on duty goes on but no requests will be near them.
 * Returns whether permission was refused and the driver's last known position (for an emergency alert).
 */
export function useRideLocation(
  onDuty: boolean,
  rideId: string | null,
): { denied: boolean; position: { latitude: number; longitude: number } | null } {
  const [denied, setDenied] = useState(false);
  const [position, setPosition] = useState<{ latitude: number; longitude: number } | null>(null);

  useEffect(() => {
    if (!onDuty) {
      return undefined;
    }

    let cancelled = false;
    let subscription: Location.LocationSubscription | null = null;

    void (async () => {
      let permission = await Location.getForegroundPermissionsAsync();
      if (permission.status !== 'granted') {
        permission = await Location.requestForegroundPermissionsAsync();
      }
      if (cancelled) {
        return;
      }
      if (permission.status !== 'granted') {
        setDenied(true);
        return;
      }

      setDenied(false);
      const started = await Location.watchPositionAsync(
        {
          accuracy: Location.Accuracy.Balanced,
          timeInterval: LOCATION_INTERVAL_MS,
          distanceInterval: LOCATION_DISTANCE_M,
        },
        (reading) => {
          const { latitude, longitude } = reading.coords;
          setPosition({ latitude, longitude });
          // A report that fails is skipped: the next one replaces it.
          rideDriverApi.shareLocation(latitude, longitude).catch(() => undefined);
          if (rideId) {
            rideDriverApi.shareRideLocation(rideId, latitude, longitude).catch(() => undefined);
          }
        },
      );
      if (cancelled) {
        started.remove();
      } else {
        subscription = started;
      }
    })().catch(() => {
      if (!cancelled) {
        setDenied(true);
      }
    });

    return () => {
      cancelled = true;
      subscription?.remove();
    };
  }, [onDuty, rideId]);

  return { denied: onDuty ? denied : false, position: onDuty ? position : null };
}
