import { useQuery } from '@tanstack/react-query';
import Constants from 'expo-constants';
import { useMemo } from 'react';

import { apiRequest } from '../../../api/apiClient';
import { isBelowMinimum } from '../appVersion';
import { effectiveModuleStatus, type ModuleKey, type ModuleStatus } from '../moduleConfig';

/** GET /api/v1/config: module flags and the minimum supported app version (spec §8). */
export interface AppConfig {
  modules: Record<string, ModuleStatus>;
  minSupportedAppVersion: string;
}

export const appConfigQueryKey = ['app-config'] as const;

/** Rechecked when the app returns to the foreground, at most every five minutes. */
export function useAppConfig() {
  return useQuery({
    queryKey: appConfigQueryKey,
    queryFn: () => apiRequest<AppConfig>('/api/v1/config'),
    staleTime: 5 * 60_000,
  });
}

export function useModuleStatus(): Record<ModuleKey, ModuleStatus> {
  const { data } = useAppConfig();
  return useMemo(() => effectiveModuleStatus(data?.modules), [data]);
}

/** Only a successful answer from the server can block the app; being offline never does. */
export function useUpdateRequired(): boolean {
  const { data } = useAppConfig();
  return isBelowMinimum(Constants.expoConfig?.version, data?.minSupportedAppVersion);
}
