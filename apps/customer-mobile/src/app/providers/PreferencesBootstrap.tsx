import { useEffect, useState, type ReactNode } from 'react';

import { preferencesRestored, showBanner, useAppDispatch, useAppState } from '../../state';
import { loadStoredPreferences, saveStoredPreferences } from '../../storage/preferencesStorage';

/**
 * Restores saved preferences behind the native splash screen, then keeps them persisted.
 * Storage side effects live here, never in reducers (spec §6). The splash screen is hidden once
 * the session has been restored too (SessionBootstrap).
 */
export function PreferencesBootstrap({ children }: { children: ReactNode }) {
  const dispatch = useAppDispatch();
  const { preferences } = useAppState();
  const [restored, setRestored] = useState(false);

  useEffect(() => {
    let active = true;
    loadStoredPreferences()
      .then((stored) => {
        if (active) dispatch(preferencesRestored(stored));
      })
      .catch(() => {
        // Storage unavailable: continue with defaults.
      })
      .finally(() => {
        if (active) setRestored(true);
      });
    return () => {
      active = false;
    };
  }, [dispatch]);

  useEffect(() => {
    if (!restored) return;
    saveStoredPreferences(preferences).catch(() => {
      dispatch(showBanner({ kind: 'error', messageKey: 'banners.preferencesSaveFailed' }));
    });
  }, [restored, preferences, dispatch]);

  return restored ? children : null;
}
