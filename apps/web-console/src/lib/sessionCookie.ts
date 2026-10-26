import type { SessionTokens, StaffRole } from './apiTypes';

// Pure helpers for the session cookies, shared by the proxy and server code (and unit tested).

export const SESSION_COOKIE = 'op_session';
export const USER_COOKIE = 'op_user';

/** Refresh this long before the access token expires, so requests never race its expiry. */
export const REFRESH_MARGIN_MS = 60_000;

export interface StoredSession {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

/** Display details only; the API decides what a person may do. */
export interface ConsoleUser {
  id: string;
  name: string;
  email: string | null;
  role: StaffRole;
}

export function toStoredSession(tokens: SessionTokens): StoredSession {
  return {
    accessToken: tokens.accessToken,
    accessTokenExpiresAt: tokens.accessTokenExpiresAt,
    refreshToken: tokens.refreshToken,
    refreshTokenExpiresAt: tokens.refreshTokenExpiresAt,
  };
}

function parseJson(value: string | undefined): unknown {
  if (!value) {
    return null;
  }
  try {
    return JSON.parse(value) as unknown;
  } catch {
    return null;
  }
}

export function parseSession(value: string | undefined): StoredSession | null {
  const data = parseJson(value) as Partial<StoredSession> | null;
  return data &&
    typeof data.accessToken === 'string' &&
    typeof data.accessTokenExpiresAt === 'string' &&
    typeof data.refreshToken === 'string' &&
    typeof data.refreshTokenExpiresAt === 'string'
    ? (data as StoredSession)
    : null;
}

const staffRoles: readonly StaffRole[] = ['administrator', 'content_editor', 'support_agent'];

export function parseUser(value: string | undefined): ConsoleUser | null {
  const data = parseJson(value) as Partial<ConsoleUser> | null;
  return data &&
    typeof data.id === 'string' &&
    typeof data.name === 'string' &&
    typeof data.role === 'string' &&
    staffRoles.includes(data.role)
    ? { id: data.id, name: data.name, email: data.email ?? null, role: data.role }
    : null;
}

export function expiresWithin(isoDate: string, milliseconds: number, now = Date.now()): boolean {
  const time = Date.parse(isoDate);
  return Number.isNaN(time) || time - now <= milliseconds;
}

export function cookieOptions(expiresAt: string, secure: boolean) {
  return {
    httpOnly: true,
    secure,
    sameSite: 'lax' as const,
    path: '/',
    expires: new Date(expiresAt),
  };
}

/** Only same-site paths, so a crafted sign-in link cannot send people elsewhere afterwards. */
export function safeNextPath(next: string | null | undefined): string {
  return next && next.startsWith('/') && !next.startsWith('//') && !next.startsWith('/\\')
    ? next
    : '/';
}
