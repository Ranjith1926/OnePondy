import { NextResponse, type NextRequest } from 'next/server';

import type { SessionTokens } from './lib/apiTypes';
import { apiBaseUrl, secureCookies } from './lib/config';
import { refreshOnce } from './lib/refresh';
import {
  cookieOptions,
  expiresWithin,
  parseSession,
  REFRESH_MARGIN_MS,
  SESSION_COOKIE,
  toStoredSession,
  USER_COOKIE,
  type StoredSession,
} from './lib/sessionCookie';

async function refreshTokens(refreshToken: string): Promise<StoredSession | null> {
  const response = await fetch(`${apiBaseUrl()}/api/v1/auth/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
    cache: 'no-store',
  });
  if (response.status === 401) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`Session refresh failed with HTTP ${response.status}`);
  }
  return toStoredSession((await response.json()) as SessionTokens);
}

function toSignIn(request: NextRequest, reason?: 'expired'): NextResponse {
  const url = new URL('/sign-in', request.url);
  url.searchParams.set('next', `${request.nextUrl.pathname}${request.nextUrl.search}`);
  if (reason) {
    url.searchParams.set('reason', reason);
  }
  const response = NextResponse.redirect(url);
  response.cookies.delete(SESSION_COOKIE);
  response.cookies.delete(USER_COOKIE);
  return response;
}

/**
 * Sends signed-out visitors to sign-in and refreshes the access token shortly before it expires.
 * Pages and server functions still check the session themselves (Next.js data security guidance).
 */
export async function proxy(request: NextRequest) {
  const session = parseSession(request.cookies.get(SESSION_COOKIE)?.value);
  if (!session || expiresWithin(session.refreshTokenExpiresAt, 0)) {
    return toSignIn(request);
  }

  if (!expiresWithin(session.accessTokenExpiresAt, REFRESH_MARGIN_MS)) {
    return NextResponse.next();
  }

  let refreshed: StoredSession | null;
  try {
    refreshed = await refreshOnce(session.refreshToken, refreshTokens);
  } catch (error) {
    // The API is unreachable: let the page load and report it, rather than signing people out.
    console.error(error);
    return NextResponse.next();
  }

  if (!refreshed) {
    return toSignIn(request, 'expired');
  }

  // Pass the new tokens to this request's server code and to the browser for the next one.
  const value = JSON.stringify(refreshed);
  request.cookies.set(SESSION_COOKIE, value);
  const response = NextResponse.next({ request: { headers: request.headers } });
  response.cookies.set(
    SESSION_COOKIE,
    value,
    cookieOptions(refreshed.refreshTokenExpiresAt, secureCookies()),
  );
  return response;
}

export const config = {
  // Everything except sign-in, the session-ended handler, photos and Next.js assets.
  matcher: ['/((?!sign-in|session-expired|media/|_next/static|_next/image|favicon.ico).*)'],
};
