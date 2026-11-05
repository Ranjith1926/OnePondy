import { onlineManager } from '@tanstack/react-query';
import { useMemo, useSyncExternalStore, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

import { ConnectivityProvider } from '../../design-system';

/**
 * Tells the design system when the phone is offline, using the same flag TanStack Query uses to pause requests, so a loading skeleton
 * says "you're offline" at exactly the moments it would otherwise wait for nothing.
 */
export function ConnectivityBridge({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const online = useSyncExternalStore(onlineManager.subscribe, () => onlineManager.isOnline());
  const value = useMemo(
    () => ({ offline: !online, offlineMessage: t('common.offlineLoading') }),
    [online, t],
  );
  return <ConnectivityProvider value={value}>{children}</ConnectivityProvider>;
}
