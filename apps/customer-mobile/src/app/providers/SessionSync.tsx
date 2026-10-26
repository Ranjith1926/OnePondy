import { useEffect } from 'react';

import { useProfile } from '../../features/auth/hooks/useProfile';
import { sessionManager } from '../../features/auth/services/sessionManager';
import { useAppDispatch, useAppState, userUpdated } from '../../state';

/**
 * Revalidates a restored session by loading the profile, and keeps the cached display name in
 * step with the server. An ended session is detected here and handled by the session manager.
 */
export function SessionSync() {
  const dispatch = useAppDispatch();
  const { auth } = useAppState();
  const profile = useProfile();
  const serverName = profile.data?.displayName;

  useEffect(() => {
    if (serverName === undefined || !auth.user || serverName === auth.user.displayName) {
      return;
    }
    dispatch(userUpdated({ displayName: serverName }));
    void sessionManager.updateUser({ ...auth.user, displayName: serverName });
  }, [serverName, auth.user, dispatch]);

  return null;
}
