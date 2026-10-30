import { isApiError } from '../../../api/apiErrors';
import type { SessionUser } from '../../../session/types';
import {
  clearSession,
  loadSession,
  saveSession,
  type StoredSession,
} from '../../../storage/sessionStorage';
import { authApi, type SessionTokens } from '../api/authApi';

/** Refresh this long before the access token expires, so requests never race its expiry. */
const REFRESH_MARGIN_MS = 60_000;

function toStored(tokens: SessionTokens): StoredSession {
  return {
    accessToken: tokens.accessToken,
    accessTokenExpiresAt: tokens.accessTokenExpiresAt,
    refreshToken: tokens.refreshToken,
    refreshTokenExpiresAt: tokens.refreshTokenExpiresAt,
    user: tokens.user,
  };
}

function expiresWithin(isoDate: string, milliseconds: number, now = Date.now()): boolean {
  return Date.parse(isoDate) - now <= milliseconds;
}

/**
 * Owns the signed-in session's tokens (memory + SecureStore). Refreshes are single-flight: the
 * server revokes a session when a rotated refresh token is reused, so two parallel refreshes
 * would sign the user out.
 */
export class SessionManager {
  private session: StoredSession | null = null;
  private refreshing: Promise<string | null> | null = null;
  private expiredListener: (() => void) | null = null;

  /** Loads a saved session; returns null when there is none or its refresh token has expired. */
  async restore(): Promise<StoredSession | null> {
    const stored = await loadSession();
    if (stored && expiresWithin(stored.refreshTokenExpiresAt, 0)) {
      await this.clear();
      return null;
    }
    this.session = stored;
    return stored;
  }

  async start(tokens: SessionTokens): Promise<void> {
    this.session = toStored(tokens);
    await saveSession(this.session);
  }

  async updateUser(user: SessionUser): Promise<void> {
    if (!this.session) {
      return;
    }
    this.session = { ...this.session, user };
    await saveSession(this.session);
  }

  async clear(): Promise<void> {
    this.session = null;
    this.refreshing = null;
    await clearSession();
  }

  get currentUser(): SessionUser | null {
    return this.session?.user ?? null;
  }

  /** Called when the server ends the session (expired, revoked or account deleted). */
  onExpired(listener: () => void): () => void {
    this.expiredListener = listener;
    return () => {
      if (this.expiredListener === listener) {
        this.expiredListener = null;
      }
    };
  }

  async getAccessToken(): Promise<string | null> {
    if (!this.session) {
      return null;
    }
    if (expiresWithin(this.session.accessTokenExpiresAt, REFRESH_MARGIN_MS)) {
      return this.refresh();
    }
    return this.session.accessToken;
  }

  refresh(): Promise<string | null> {
    if (!this.session) {
      return Promise.resolve(null);
    }
    this.refreshing ??= this.performRefresh().finally(() => {
      this.refreshing = null;
    });
    return this.refreshing;
  }

  private async performRefresh(): Promise<string | null> {
    const current = this.session;
    if (!current) {
      return null;
    }

    try {
      const tokens = await authApi.refresh(current.refreshToken);
      if (this.session !== current) {
        // Signed out while the refresh was in flight.
        return this.session?.accessToken ?? null;
      }
      this.session = toStored(tokens);
      await saveSession(this.session);
      return this.session.accessToken;
    } catch (error) {
      if (this.session !== current) {
        // Signed out (and maybe in again) meanwhile: this failure belongs to the old session.
        return this.session?.accessToken ?? null;
      }
      if (isApiError(error) && error.status === 401) {
        await this.clear();
        this.expiredListener?.();
        return null;
      }
      // Offline or server error: keep the session and let the caller report the failure.
      throw error;
    }
  }
}

export const sessionManager = new SessionManager();
