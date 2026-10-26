import type { StoredSession } from './sessionCookie';

type Refresh = (refreshToken: string) => Promise<StoredSession | null>;

/** How long a refresh result is reused for requests that still carry the old refresh token. */
const REUSE_WINDOW_MS = 60_000;

const recent = new Map<string, { result: Promise<StoredSession | null>; until: number }>();

/**
 * Refreshes once per refresh token. Refresh tokens rotate, and the API revokes the whole session when
 * an old one is reused, so parallel page loads must share one refresh instead of each sending the
 * same token. Results are kept briefly for requests that started before the new cookie arrived.
 * Assumes one console process (spec §13); several instances would need sticky sessions.
 */
export function refreshOnce(
  refreshToken: string,
  refresh: Refresh,
  now = Date.now(),
): Promise<StoredSession | null> {
  for (const [token, entry] of recent) {
    if (entry.until <= now) {
      recent.delete(token);
    }
  }

  const existing = recent.get(refreshToken);
  if (existing) {
    return existing.result;
  }

  const result = refresh(refreshToken);
  recent.set(refreshToken, { result, until: now + REUSE_WINDOW_MS });
  return result;
}

/** For tests. */
export function resetRefreshCache(): void {
  recent.clear();
}
