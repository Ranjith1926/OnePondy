import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { isApiError } from '../../../api/apiErrors';
import { useSession } from '../../../session/SessionProvider';
import { driverApi, type DriverDetails } from '../api/driverApi';

export const driverKeys = { me: ['driver', 'me'] as const };

/** The signed-in driver's application and duty. Disabled while signed out. */
export function useDriverProfile() {
  const { status } = useSession();
  return useQuery({
    queryKey: driverKeys.me,
    queryFn: driverApi.getMine,
    enabled: status === 'signedIn',
  });
}

/** After "changed since you loaded it", show what is there now instead of asking the driver to guess. */
function useReloadOnConflict() {
  const queryClient = useQueryClient();
  return (error: unknown) => {
    if (isApiError(error) && error.code === 'driver_changed') {
      void queryClient.invalidateQueries({ queryKey: driverKeys.me });
    }
  };
}

/** Applies to drive, or sends changed details for review. */
export function useSaveDriver() {
  const queryClient = useQueryClient();
  const reloadOnConflict = useReloadOnConflict();
  return useMutation({
    mutationFn: (details: DriverDetails) => driverApi.save(details),
    onSuccess: (profile) => queryClient.setQueryData(driverKeys.me, profile),
    onError: reloadOnConflict,
  });
}

/** Starts or ends a shift. */
export function useSetDuty() {
  const queryClient = useQueryClient();
  const reloadOnConflict = useReloadOnConflict();
  return useMutation({
    mutationFn: ({ onDuty, version }: { onDuty: boolean; version: number }) =>
      driverApi.setDuty(onDuty, version),
    onSuccess: (profile) => queryClient.setQueryData(driverKeys.me, profile),
    onError: reloadOnConflict,
  });
}
