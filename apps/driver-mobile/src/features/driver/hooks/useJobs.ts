import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import * as Location from 'expo-location';
import { useEffect, useState } from 'react';

import { driverApi, type JobDetail } from '../api/driverApi';

export const jobKeys = { all: ['driver', 'jobs'] as const };

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
export const useDeliverJob = () => useJobStep(driverApi.deliverJob);

/** How often, and how far apart, the driver's position is reported while they hold a delivery. */
const LOCATION_INTERVAL_MS = 15_000;
const LOCATION_DISTANCE_M = 25;

/**
 * Reports the driver's position to OnePondy for as long as they hold a delivery, and not a moment longer: with no delivery
 * (<paramref name="jobId"/> null) nothing is requested or collected. Only foreground location is used, so nothing is reported while
 * the app is closed. If the driver says no to location, the delivery goes on without it. Returns whether permission was refused.
 */
export function useShareLocation(jobId: string | null): { denied: boolean } {
  const [denied, setDenied] = useState(false);

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
          accuracy: Location.Accuracy.Balanced,
          timeInterval: LOCATION_INTERVAL_MS,
          distanceInterval: LOCATION_DISTANCE_M,
        },
        (position) => {
          // A report that fails is skipped: the next one replaces it.
          driverApi
            .shareLocation(jobId, position.coords.latitude, position.coords.longitude)
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

  return { denied: jobId ? denied : false };
}
