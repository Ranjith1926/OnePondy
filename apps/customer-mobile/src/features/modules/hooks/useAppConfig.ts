import { useQuery } from '@tanstack/react-query';
import Constants from 'expo-constants';
import { useEffect, useMemo, useState } from 'react';

import { apiRequest } from '../../../api/apiClient';
import { loadStoredModuleFlags, saveStoredModuleFlags } from '../../../storage/moduleFlagsStorage';
import { isBelowMinimum } from '../appVersion';
import { effectiveModuleStatus, type ModuleKey, type ModuleStatus } from '../moduleConfig';

/** GET /api/v1/config: module flags and the minimum supported app version (spec §8). */
export interface AppConfig {
  modules: Record<string, ModuleStatus>;
  minSupportedAppVersion: string;
}

export const appConfigQueryKey = ['app-config'] as const;

/** How long to wait before asking again while the server has not answered at all. */
export const configRetryMs = 10_000;

/**
 * Rechecked when the app returns to the foreground, at most every five minutes. Until the first answer
 * arrives it keeps asking, so one slow first request (a server waking up, a weak signal) cannot leave
 * the home screen stuck on "coming soon".
 */
export function useAppConfig() {
  return useQuery({
    queryKey: appConfigQueryKey,
    queryFn: async () => {
      const config = await apiRequest<AppConfig>('/api/v1/config');
      saveStoredModuleFlags(config.modules).catch(() => {
        // Storage unavailable: the flags are simply not remembered.
      });
      return config;
    },
    staleTime: 5 * 60_000,
    retry: 4,
    retryDelay: (attempt) => Math.min(1000 * 2 ** attempt, 8000),
    refetchInterval: (query) => (query.state.data === undefined ? configRetryMs : false),
  });
}

/** The flags from the last time the server answered, read once from the phone. */
function useRememberedFlags(): Record<string, string> | undefined {
  const [remembered, setRemembered] = useState<Record<string, string> | undefined>();
  useEffect(() => {
    let active = true;
    loadStoredModuleFlags()
      .then((flags) => {
        if (active) setRemembered(flags);
      })
      .catch(() => {
        // Storage unavailable: nothing remembered.
      });
    return () => {
      active = false;
    };
  }, []);
  return remembered;
}

export function useModuleStatus(): Record<ModuleKey, ModuleStatus> {
  const { data } = useAppConfig();
  const remembered = useRememberedFlags();
  return useMemo(() => effectiveModuleStatus(data?.modules ?? remembered), [data, remembered]);
}

/**
 * True once the server has failed to answer and the phone has nothing remembered from an earlier answer.
 * It counts failures instead of reading the error state, which goes back to "pending" every time the
 * periodic retry starts and would make the notice flicker.
 */
export function useConfigUnreachable(): boolean {
  const { data, errorUpdateCount } = useAppConfig();
  const remembered = useRememberedFlags();
  return data === undefined && errorUpdateCount > 0 && remembered === undefined;
}

/** Only a successful answer from the server can block the app; being offline never does. */
export function useUpdateRequired(): boolean {
  const { data } = useAppConfig();
  return isBelowMinimum(Constants.expoConfig?.version, data?.minSupportedAppVersion);
}
