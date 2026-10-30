import * as SecureStore from 'expo-secure-store';

import type { SessionUser } from '../session/types';

// Tokens and session material live only in SecureStore, never in app state (spec §6, §12).
const STORAGE_KEY = 'onepondy.session.v1';

export interface StoredSession {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: SessionUser;
}

function isStoredSession(value: unknown): value is StoredSession {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const session = value as Record<string, unknown>;
  const user = session.user as Record<string, unknown> | undefined;
  return (
    typeof session.accessToken === 'string' &&
    typeof session.accessTokenExpiresAt === 'string' &&
    typeof session.refreshToken === 'string' &&
    typeof session.refreshTokenExpiresAt === 'string' &&
    typeof user?.id === 'string' &&
    typeof user.displayName === 'string' &&
    Array.isArray(user.roles)
  );
}

export async function loadSession(): Promise<StoredSession | null> {
  const raw = await SecureStore.getItemAsync(STORAGE_KEY);
  if (!raw) {
    return null;
  }
  try {
    const parsed: unknown = JSON.parse(raw);
    return isStoredSession(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

export async function saveSession(session: StoredSession): Promise<void> {
  await SecureStore.setItemAsync(STORAGE_KEY, JSON.stringify(session));
}

export async function clearSession(): Promise<void> {
  await SecureStore.deleteItemAsync(STORAGE_KEY);
}
