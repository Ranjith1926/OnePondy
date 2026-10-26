import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { PlaceDetail } from './apiTypes';
import { formDefaults, placeInputFromForm } from './placeForm';
import { refreshOnce, resetRefreshCache } from './refresh';
import { canEditContent, isAdministrator, staffRoleFrom } from './roles';
import {
  expiresWithin,
  parseSession,
  parseUser,
  safeNextPath,
  type StoredSession,
} from './sessionCookie';

const session: StoredSession = {
  accessToken: 'access-1',
  accessTokenExpiresAt: '2026-10-26T10:15:00Z',
  refreshToken: 'refresh-1',
  refreshTokenExpiresAt: '2026-11-25T10:00:00Z',
};

describe('session cookies', () => {
  it('reads only well-formed sessions and staff users', () => {
    expect(parseSession(JSON.stringify(session))).toEqual(session);
    expect(parseSession('{"accessToken":1}')).toBeNull();
    expect(parseSession('not json')).toBeNull();
    expect(parseUser(JSON.stringify({ id: 'u1', name: 'Asha', role: 'content_editor' }))).toEqual({
      id: 'u1',
      name: 'Asha',
      email: null,
      role: 'content_editor',
    });
    expect(parseUser(JSON.stringify({ id: 'u1', name: 'Asha', role: 'customer' }))).toBeNull();
  });

  it('treats a token as expiring within the margin, and an unreadable date as expired', () => {
    const now = Date.parse('2026-10-26T10:14:30Z');
    expect(expiresWithin(session.accessTokenExpiresAt, 60_000, now)).toBe(true);
    expect(expiresWithin(session.accessTokenExpiresAt, 10_000, now)).toBe(false);
    expect(expiresWithin('soon', 0, now)).toBe(true);
  });

  it.each([
    ['/places/1?saved=1', '/places/1?saved=1'],
    ['https://evil.example', '/'],
    ['//evil.example', '/'],
    ['/\\evil.example', '/'],
    [undefined, '/'],
  ])('only redirects to same-site paths after sign-in (%s)', (next, expected) => {
    expect(safeNextPath(next)).toBe(expected);
  });
});

describe('refreshOnce', () => {
  beforeEach(() => resetRefreshCache());

  it('shares one refresh between parallel requests carrying the same token', async () => {
    const refreshed = { ...session, accessToken: 'access-2', refreshToken: 'refresh-2' };
    const refresh = vi.fn().mockResolvedValue(refreshed);

    const results = await Promise.all([
      refreshOnce('refresh-1', refresh),
      refreshOnce('refresh-1', refresh),
    ]);

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(results).toEqual([refreshed, refreshed]);
  });

  it('refreshes again once the reuse window has passed', async () => {
    const refresh = vi.fn().mockResolvedValue(null);

    await refreshOnce('refresh-1', refresh, 0);
    await refreshOnce('refresh-1', refresh, 61_000);

    expect(refresh).toHaveBeenCalledTimes(2);
  });
});

describe('roles', () => {
  it('maps session roles to what the console shows', () => {
    expect(staffRoleFrom(['customer', 'support_agent', 'administrator'])).toBe('administrator');
    expect(staffRoleFrom(['customer'])).toBeNull();
    expect(canEditContent('support_agent')).toBe(false);
    expect(canEditContent('content_editor')).toBe(true);
    expect(isAdministrator('content_editor')).toBe(false);
  });
});

function form(fields: Record<string, string>): FormData {
  const data = new FormData();
  for (const [key, value] of Object.entries(fields)) {
    data.set(key, value);
  }
  return data;
}

describe('placeInputFromForm', () => {
  it('builds the API input from the form fields', () => {
    const { input, errors } = placeInputFromForm(
      form({
        name: '  Paradise Beach ',
        categoryCode: 'beach',
        description: 'Reached by boat.',
        latitude: '11,8915',
        longitude: '79.8257',
        'hours-monday-0-opens': '08:00',
        'hours-monday-0-closes': '17:30',
        hoursNotes: '',
        'fee-standard': '50',
        'fee-child': '0',
        'fee-senior': '',
        'ta-name': 'பாரடைஸ் கடற்கரை',
        'fr-name': '',
      }),
    );

    expect(errors).toEqual({});
    expect(input).toMatchObject({
      name: 'Paradise Beach',
      latitude: 11.8915,
      longitude: 79.8257,
      openingHours: {
        alwaysOpen: false,
        periods: [{ day: 'monday', opens: '08:00', closes: '17:30' }],
        notes: null,
      },
      entryFees: [
        { category: 'standard', amount: 50 },
        { category: 'child', amount: 0 },
      ],
      translations: [
        { language: 'ta', name: 'பாரடைஸ் கடற்கரை', description: null, visitorGuidance: null },
      ],
    });
  });

  it('reports values that cannot be sent', () => {
    const { input, errors } = placeInputFromForm(
      form({ latitude: 'north', 'hours-tuesday-1-opens': '14:00', 'fee-student': '-5' }),
    );

    expect(Object.keys(errors).sort()).toEqual(['entryFees', 'latitude', 'openingHours']);
    expect(input.openingHours).toBeNull();
  });

  it('pre-fills an existing place', () => {
    const defaults = formDefaults({
      openingHours: {
        alwaysOpen: false,
        notes: null,
        periods: [
          { day: 'monday', opens: '09:00', closes: '12:00' },
          { day: 'monday', opens: '14:00', closes: '17:00' },
        ],
      },
      entryFees: [{ category: 'standard', amount: 25 }],
      translations: [{ language: 'fr', name: 'Plage', description: null, visitorGuidance: null }],
    } as unknown as PlaceDetail);

    expect(defaults.period('monday', 1, 'opens')).toBe('14:00');
    expect(defaults.period('tuesday', 0, 'opens')).toBe('');
    expect(defaults.fee('standard')).toBe('25');
    expect(defaults.translation('fr', 'name')).toBe('Plage');
  });
});
