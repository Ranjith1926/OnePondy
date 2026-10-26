import 'server-only';

import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';

import type { SessionTokens } from './apiTypes';
import { secureCookies } from './config';
import { canEditContent, isAdministrator } from './roles';
import {
  cookieOptions,
  parseSession,
  parseUser,
  SESSION_COOKIE,
  toStoredSession,
  USER_COOKIE,
  type ConsoleUser,
  type StoredSession,
} from './sessionCookie';

export async function getSession(): Promise<StoredSession | null> {
  return parseSession((await cookies()).get(SESSION_COOKIE)?.value);
}

export async function getUser(): Promise<ConsoleUser | null> {
  const store = await cookies();
  return parseSession(store.get(SESSION_COOKIE)?.value)
    ? parseUser(store.get(USER_COOKIE)?.value)
    : null;
}

/** For pages and server functions: every one checks the session itself, not only the proxy. */
export async function requireUser(): Promise<ConsoleUser> {
  const user = await getUser();
  if (!user) {
    redirect('/sign-in');
  }
  return user;
}

export async function requireEditor(): Promise<ConsoleUser> {
  const user = await requireUser();
  if (!canEditContent(user.role)) {
    redirect('/?denied=1');
  }
  return user;
}

export async function requireAdministrator(): Promise<ConsoleUser> {
  const user = await requireUser();
  if (!isAdministrator(user.role)) {
    redirect('/?denied=1');
  }
  return user;
}

/** Only from server functions and route handlers, which can set cookies. */
export async function saveSession(tokens: SessionTokens, user: ConsoleUser): Promise<void> {
  const store = await cookies();
  const options = cookieOptions(tokens.refreshTokenExpiresAt, secureCookies());
  store.set(SESSION_COOKIE, JSON.stringify(toStoredSession(tokens)), options);
  store.set(USER_COOKIE, JSON.stringify(user), options);
}

export async function clearSession(): Promise<void> {
  const store = await cookies();
  store.delete(SESSION_COOKIE);
  store.delete(USER_COOKIE);
}
