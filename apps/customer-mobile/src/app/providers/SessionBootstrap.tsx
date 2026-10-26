import { useQueryClient } from '@tanstack/react-query';
import * as SplashScreen from 'expo-splash-screen';
import { useEffect, type ReactNode } from 'react';

import { configureApiAuth } from '../../api/apiClient';
import { sessionManager } from '../../features/auth/services/sessionManager';
import {
  sessionCleared,
  sessionRestored,
  showBanner,
  useAppDispatch,
  useAppState,
} from '../../state';

/**
 * Restores a saved session from SecureStore before the first screen renders, connects the API
 * client to the session's tokens and handles the server ending the session (spec §7).
 */
export function SessionBootstrap({ children }: { children: ReactNode }) {
  const dispatch = useAppDispatch();
  const queryClient = useQueryClient();
  const { auth } = useAppState();

  useEffect(() => {
    configureApiAuth({
      getAccessToken: () => sessionManager.getAccessToken(),
      refreshAccessToken: () => sessionManager.refresh(),
    });
    const unsubscribe = sessionManager.onExpired(() => {
      queryClient.clear();
      dispatch(sessionCleared());
      dispatch(showBanner({ kind: 'warning', messageKey: 'banners.sessionExpired' }));
    });

    let active = true;
    sessionManager
      .restore()
      .then((stored) => {
        if (active) dispatch(stored ? sessionRestored(stored.user) : sessionCleared());
      })
      .catch(() => {
        if (active) dispatch(sessionCleared());
      });

    return () => {
      active = false;
      unsubscribe();
      configureApiAuth(null);
    };
  }, [dispatch, queryClient]);

  const ready = auth.status !== 'loading';
  useEffect(() => {
    if (ready) SplashScreen.hide();
  }, [ready]);

  return ready ? children : null;
}
