import { useQueryClient } from '@tanstack/react-query';
import * as SplashScreen from 'expo-splash-screen';
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';

import { configureApiAuth } from '../api/apiClient';
import { authApi, type SessionTokens } from '../features/auth/api/authApi';
import { sessionManager } from '../features/auth/services/sessionManager';
import type { SessionUser } from './types';

export type SessionStatus = 'loading' | 'signedOut' | 'signedIn';

interface SessionContextValue {
  status: SessionStatus;
  user: SessionUser | null;
  /** True when the server ended the session (as opposed to the driver signing out). */
  expired: boolean;
  startSession: (tokens: SessionTokens) => Promise<void>;
  signOut: () => Promise<void>;
}

const SessionContext = createContext<SessionContextValue | null>(null);

export function useSession(): SessionContextValue {
  const value = useContext(SessionContext);
  if (!value) {
    throw new Error('useSession must be used inside <SessionProvider>.');
  }
  return value;
}

/**
 * Restores a saved session from SecureStore before the first screen renders, connects the API
 * client to the session's tokens and handles the server ending the session (spec §7).
 */
export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<SessionStatus>('loading');
  const [user, setUser] = useState<SessionUser | null>(null);
  const [expired, setExpired] = useState(false);

  useEffect(() => {
    configureApiAuth({
      getAccessToken: () => sessionManager.getAccessToken(),
      refreshAccessToken: () => sessionManager.refresh(),
    });
    const unsubscribe = sessionManager.onExpired(() => {
      queryClient.clear();
      setUser(null);
      setExpired(true);
      setStatus('signedOut');
    });

    let active = true;
    sessionManager
      .restore()
      .then((stored) => {
        if (!active) return;
        setUser(stored?.user ?? null);
        setStatus(stored ? 'signedIn' : 'signedOut');
      })
      .catch(() => {
        if (active) setStatus('signedOut');
      });

    return () => {
      active = false;
      unsubscribe();
      configureApiAuth(null);
    };
  }, [queryClient]);

  useEffect(() => {
    if (status !== 'loading') {
      void SplashScreen.hideAsync();
    }
  }, [status]);

  const startSession = useCallback(async (tokens: SessionTokens) => {
    await sessionManager.start(tokens);
    setUser(tokens.user);
    setExpired(false);
    setStatus('signedIn');
  }, []);

  const signOut = useCallback(async () => {
    // While the session is still valid, tell the server this phone is done; the local session is cleared either way.
    try {
      await authApi.signOut();
    } catch {
      // Best effort: the refresh token expires on its own.
    }
    await sessionManager.clear();
    queryClient.clear();
    setUser(null);
    setExpired(false);
    setStatus('signedOut');
  }, [queryClient]);

  const value = useMemo(
    () => ({ status, user, expired, startSession, signOut }),
    [status, user, expired, startSession, signOut],
  );

  return (
    <SessionContext.Provider value={value}>
      {status === 'loading' ? null : children}
    </SessionContext.Provider>
  );
}
