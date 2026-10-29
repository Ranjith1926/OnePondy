import { NextResponse, type NextRequest } from 'next/server';

import type { SessionTokens } from './lib/apiTypes';
import { apiBaseUrl, secureCookies } from './lib/config';
import { refreshOnce } from './lib/refresh';
import {
  cookieOptions,
  expiresWithin,
  parseSession,
  PARTNER_SESSION_COOKIE,
  PARTNER_USER_COOKIE,
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

interface SessionCookies {
  session: string;
  user: string;
  signInPath: string;
}

const staffCookies: SessionCookies = {
  session: SESSION_COOKIE,
  user: USER_COOKIE,
  signInPath: '/sign-in',
};
const partnerCookies: SessionCookies = {
  session: PARTNER_SESSION_COOKIE,
  user: PARTNER_USER_COOKIE,
  signInPath: '/partner/sign-in',
};

function toSignIn(request: NextRequest, cookies: SessionCookies, reason?: 'expired'): NextResponse {
  const url = new URL(cookies.signInPath, request.url);
  url.searchParams.set('next', `${request.nextUrl.pathname}${request.nextUrl.search}`);
  if (reason) {
    url.searchParams.set('reason', reason);
  }
  const response = NextResponse.redirect(url);
  response.cookies.delete(cookies.session);
  response.cookies.delete(cookies.user);
  return response;
}

/**
 * Sends signed-out visitors to sign-in and refreshes the access token shortly before it expires.
 * Pages and server functions still check the session themselves (Next.js data security guidance).
 * Shared between the staff session and the partner session (spec §1: a partner is a customer
 * account, not a staff one, so it gets its own cookie pair) — same refresh endpoint either way.
 */
async function handleSession(request: NextRequest, cookies: SessionCookies) {
  const session = parseSession(request.cookies.get(cookies.session)?.value);
  if (!session || expiresWithin(session.refreshTokenExpiresAt, 0)) {
    return toSignIn(request, cookies);
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
    return toSignIn(request, cookies, 'expired');
  }

  // Pass the new tokens to this request's server code and to the browser for the next one.
  const value = JSON.stringify(refreshed);
  request.cookies.set(cookies.session, value);
  const response = NextResponse.next({ request: { headers: request.headers } });
  response.cookies.set(
    cookies.session,
    value,
    cookieOptions(refreshed.refreshTokenExpiresAt, secureCookies()),
  );
  return response;
}

export async function proxy(request: NextRequest) {
  return handleSession(
    request,
    request.nextUrl.pathname.startsWith('/partner') ? partnerCookies : staffCookies,
  );
}

export const config = {
  // Everything except both sign-in pages, the session-ended handler, photos and Next.js assets.
  matcher: [
    '/((?!sign-in|partner/sign-in|session-expired|media/|_next/static|_next/image|favicon.ico).*)',
  ],
};
