import { useEffect } from 'react';

import { useAppState } from '../../../state';
import { expoPushPlatform, pushApi, pushStore } from '../expoPushPlatform';
import { registerForPush, unregisterFromPush } from '../pushRegistration';

/**
 * Registers the phone for notifications each time the customer is signed in, but only if they have already allowed them:
 * this never asks. Asking happens once, after their first booking (see `askForPushAfterBooking`).
 */
export function useRegisterPushWhenSignedIn(): void {
  const { auth } = useAppState();
  const signedIn = auth.status === 'authenticated';

  useEffect(() => {
    if (signedIn) {
      void registerForPush({
        platform: expoPushPlatform,
        api: pushApi,
        store: pushStore,
        askIfNeeded: false,
      });
    }
  }, [signedIn]);
}

/** The customer has just made their first booking, which is when a notification becomes worth having: ask once. */
export function askForPushAfterBooking(): Promise<unknown> {
  return registerForPush({
    platform: expoPushPlatform,
    api: pushApi,
    store: pushStore,
    askIfNeeded: true,
  });
}

/** On sign-out: tell the server this phone is done, and forget it here. */
export function forgetPushRegistration(): Promise<void> {
  return unregisterFromPush({ api: pushApi, store: pushStore });
}
