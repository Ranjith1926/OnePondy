import NetInfo from '@react-native-community/netinfo';
import {
  focusManager,
  onlineManager,
  QueryClient,
  QueryClientProvider,
} from '@tanstack/react-query';
import { useEffect, useState, type ReactNode } from 'react';
import { AppState } from 'react-native';

import { isApiError } from '../../api/apiErrors';

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // Retry network and server failures, never client errors such as 401 or 404 (spec §8).
        retry: (failureCount, error) =>
          !(isApiError(error) && error.status < 500) && failureCount < 2,
        staleTime: 30_000,
      },
      // Writes are never retried automatically, so a payment or booking cannot be submitted twice.
      mutations: { retry: false },
    },
  });
}

let onlineListenerInstalled = false;

function installOnlineListener(): void {
  if (onlineListenerInstalled) {
    return;
  }
  onlineListenerInstalled = true;
  onlineManager.setEventListener((setOnline) =>
    NetInfo.addEventListener((state) => setOnline(state.isConnected !== false)),
  );
}

/** TanStack Query for server state, aware of connectivity and app focus. */
export function QueryProvider({ children }: { children: ReactNode }) {
  const [client] = useState(createQueryClient);

  useEffect(() => {
    installOnlineListener();
    const subscription = AppState.addEventListener('change', (status) =>
      focusManager.setFocused(status === 'active'),
    );
    return () => subscription.remove();
  }, []);

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
