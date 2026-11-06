import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import * as Location from 'expo-location';
import { useEffect, useState } from 'react';

import { driverApi, type JobDetail } from '../api/driverApi';

export const jobKeys = { all: ['driver', 'jobs'] as const, cash: ['driver', 'cash'] as const };

/** How often an approved driver's list of deliveries refreshes by itself while the app is open. */
const REFRESH_MS = 10_000;

/** The delivery the driver holds and, while they are free and on duty, the open ones. */
export function useJobs(enabled: boolean) {
  return useQuery({
    queryKey: jobKeys.all,
    queryFn: driverApi.jobs,
    enabled,
    refetchInterval: REFRESH_MS,
  });
}

/** One step on a delivery. Whatever happens, the list is reloaded, so a driver who lost a race sees what is true now. */
function useJobStep(step: (id: string) => Promise<JobDetail>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: step,
    onSettled: () => queryClient.invalidateQueries({ queryKey: jobKeys.all }),
  });
}

export const useAcceptJob = () => useJobStep(driverApi.acceptJob);
export const useReleaseJob = () => useJobStep(driverApi.releaseJob);
export const usePickUpJob = () => useJobStep(driverApi.pickUpJob);

/** Hands the order over. For a cash order the driver confirms the cash they took; the cash they hold is then read again. */
export function useDeliverJob() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, cashCollected }: { id: string; cashCollected?: number }) =>
      driverApi.deliverJob(id, cashCollected),
    onSettled: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: jobKeys.all }),
        queryClient.invalidateQueries({ queryKey: jobKeys.cash }),
      ]),
  });
}

/** The cash the driver holds for OnePondy. */
export function useDriverCash(enabled: boolean) {
  return useQuery({
    queryKey: jobKeys.cash,
    queryFn: driverApi.cash,
    enabled,
    refetchInterval: 30_000,
  });
}

/** How often, and how far apart, the driver's position is reported while they hold a delivery. */
const LOCATION_INTERVAL_MS = 15_000;
const LOCATION_DISTANCE_M = 25;

/**
 * Reports the driver's position to OnePondy for as long as they hold a delivery, and not a moment longer: with no delivery
 * (<paramref name="jobId"/> null) nothing is requested or collected. Only foreground location is used, so nothing is reported while
 * the app is closed. If the driver says no to location, the delivery goes on without it. Returns whether permission was refused and the latest position, which stays on the phone
 * unless it is reported to OnePondy as above.
 */
export function useShareLocation(jobId: string | null): {
  denied: boolean;
  position: { latitude: number; longitude: number } | null;
} {
  const [denied, setDenied] = useState(false);
  const [position, setPosition] = useState<{ latitude: number; longitude: number } | null>(null);

  useEffect(() => {
    if (!jobId) {
      return;
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
          accuracy: Location.Accuracy.High,
          timeInterval: LOCATION_INTERVAL_MS,
          distanceInterval: LOCATION_DISTANCE_M,
        },
        (reading) => {
          setPosition({ latitude: reading.coords.latitude, longitude: reading.coords.longitude });
          // A report that fails is skipped: the next one replaces it.
          driverApi
            .shareLocation(jobId, reading.coords.latitude, reading.coords.longitude)
            .catch(() => undefined);
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
  }, [jobId]);

  return { denied: jobId ? denied : false, position: jobId ? position : null };
}
