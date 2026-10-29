import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';

import {
  sessionCleared,
  showBanner,
  signedIn,
  useAppDispatch,
  type BannerMessageKey,
  type SessionUser,
} from '../../../state';
import { forgetPushRegistration } from '../../notifications/hooks/usePushSetup';
import { authApi } from '../api/authApi';
import { sessionManager } from '../services/sessionManager';

export function useAuthActions() {
  const dispatch = useAppDispatch();
  const queryClient = useQueryClient();

  /** Marks the user signed in; the sign-in screens close and the previous screen shows again. */
  const finishSignIn = useCallback((user: SessionUser) => dispatch(signedIn(user)), [dispatch]);

  /** Clears tokens, cached server data and the session summary (spec §7, step 9). */
  const endSession = useCallback(
    async (messageKey: BannerMessageKey, kind: 'info' | 'success') => {
      await sessionManager.clear();
      queryClient.clear();
      dispatch(sessionCleared());
      dispatch(showBanner({ kind, messageKey }));
    },
    [dispatch, queryClient],
  );

  const signOut = useCallback(async () => {
    // While the session is still valid, tell the server this phone is done. The server also stops using it when the session ends.
    await forgetPushRegistration();
    try {
      await authApi.signOut();
    } catch {
      // Best effort: the local session is cleared either way and the refresh token expires.
    }
    await endSession('banners.signedOut', 'info');
  }, [endSession]);

  const endSessionAfterDeletion = useCallback(
    () => endSession('banners.accountDeleted', 'success'),
    [endSession],
  );

  return { finishSignIn, signOut, endSessionAfterDeletion };
}
