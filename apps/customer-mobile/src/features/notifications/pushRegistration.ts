/** What the phone and the OS provide, so registration can be tested without a device. */
export interface PushPlatform {
  os: 'android' | 'ios' | 'other';
  /** The Expo project the push token belongs to. Without one (no Expo project set up yet) push cannot work. */
  projectId: string | null;
  getPermission: () => Promise<'granted' | 'denied' | 'undetermined'>;
  requestPermission: () => Promise<'granted' | 'denied'>;
  /** Android needs a notification channel before it will show anything. */
  ensureChannel: () => Promise<void>;
  getToken: (projectId: string) => Promise<string>;
}

export interface PushApi {
  register: (token: string, platform: 'android' | 'ios') => Promise<{ id: string }>;
  unregister: (deviceId: string) => Promise<void>;
}

export interface StoredRegistration {
  deviceId: string;
  token: string;
}

export interface PushStore {
  get: () => Promise<StoredRegistration | null>;
  set: (value: StoredRegistration) => Promise<void>;
  clear: () => Promise<void>;
  /** Whether the OS permission prompt has already been shown by the app. It is asked once, never nagged. */
  hasAskedForPermission: () => Promise<boolean>;
  markAskedForPermission: () => Promise<void>;
}

export type RegistrationResult =
  | 'registered'
  /** Notifications cannot work on this build or device (no Expo project, or not a phone). Not an error. */
  | 'unavailable'
  /** Permission has not been given and the app is not asking right now. */
  | 'permission-needed'
  | 'permission-denied'
  | 'failed';

export interface RegisterOptions {
  platform: PushPlatform;
  api: PushApi;
  store: PushStore;
  /** True only when the customer has just done something that makes a notification worthwhile (their first booking). */
  askIfNeeded: boolean;
}

/**
 * Registers this phone for push notifications when it is allowed and possible. It never asks the customer for permission
 * unless told to, and asks at most once. Registering is tied to the current sign-in, so it is repeated on each sign-in.
 */
export async function registerForPush({
  platform,
  api,
  store,
  askIfNeeded,
}: RegisterOptions): Promise<RegistrationResult> {
  if ((platform.os !== 'android' && platform.os !== 'ios') || platform.projectId === null) {
    return 'unavailable';
  }

  try {
    let permission = await platform.getPermission();
    if (permission === 'undetermined') {
      if (!askIfNeeded || (await store.hasAskedForPermission())) {
        return 'permission-needed';
      }

      // Android 13 and later show the prompt only once a channel exists.
      await platform.ensureChannel();
      await store.markAskedForPermission();
      permission = await platform.requestPermission();
    }

    if (permission !== 'granted') {
      return 'permission-denied';
    }

    await platform.ensureChannel();
    const token = await platform.getToken(platform.projectId);
    const registered = await api.register(token, platform.os);
    await store.set({ deviceId: registered.id, token });
    return 'registered';
  } catch {
    // Offline, no Google services on this build, or the server said no. The next sign-in tries again.
    return 'failed';
  }
}

/** Forgets this phone on the server and here. Best effort: the server also stops using it when the session ends. */
export async function unregisterFromPush({
  api,
  store,
}: Pick<RegisterOptions, 'api' | 'store'>): Promise<void> {
  try {
    const stored = await store.get();
    if (stored) {
      await api.unregister(stored.deviceId);
    }
  } catch {
    // The session ending is what really stops notifications; there is nothing more to do.
  }

  await store.clear();
}
