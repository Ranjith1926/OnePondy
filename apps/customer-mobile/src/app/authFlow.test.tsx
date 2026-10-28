import AsyncStorage from '@react-native-async-storage/async-storage';
import { render, screen, userEvent, waitFor } from '@testing-library/react-native';

import { sessionManager } from '../features/auth/services/sessionManager';
import type { SessionTokens } from '../features/auth/api/authApi';
import { loadSession } from '../storage/sessionStorage';
import App from './App';

const tab = (label: string) => screen.getByRole('button', { name: new RegExp(`^${label}, tab`) });

const minutesFromNow = (minutes: number) => new Date(Date.now() + minutes * 60_000).toISOString();

function tokensFor(displayName: string, isNewUser: boolean): SessionTokens {
  return {
    accessToken: 'access-1',
    accessTokenExpiresAt: minutesFromNow(15),
    refreshToken: 'refresh-1',
    refreshTokenExpiresAt: minutesFromNow(30 * 24 * 60),
    user: { id: 'u1', displayName, roles: ['customer'] },
    isNewUser,
  };
}

const profile = {
  id: 'u1',
  displayName: 'Asha',
  phoneNumber: '+919876543210',
  email: null,
  preferredLanguage: 'en',
  createdAt: '2026-10-26T04:00:00Z',
};

type Handler = (body: unknown) => { status: number; body?: unknown };
let routes: Record<string, Handler>;
let requests: { route: string; body: unknown; authorization: string | null }[];

function installFakeApi() {
  requests = [];
  globalThis.fetch = jest.fn((input: string, init: RequestInit) => {
    const route = `${init.method ?? 'GET'} ${new URL(input).pathname}`;
    const body = init.body ? JSON.parse(init.body as string) : undefined;
    const headers = init.headers as Record<string, string>;
    // Every launch reads the public app config and the home screen's popular places; these tests
    // track account requests only.
    if (route !== 'GET /api/v1/config' && route !== 'GET /api/v1/places') {
      requests.push({ route, body, authorization: headers.Authorization ?? null });
    }
    const handler = routes[route];
    const { status, body: responseBody } = handler ? handler(body) : { status: 404 };
    const text = responseBody === undefined ? '' : JSON.stringify(responseBody);
    return Promise.resolve({
      ok: status >= 200 && status < 300,
      status,
      text: () => Promise.resolve(text),
    });
  }) as unknown as typeof fetch;
}

async function launch() {
  await AsyncStorage.setItem(
    'onepondy.preferences.v1',
    JSON.stringify({ language: 'en', theme: 'system', onboardingCompleted: true }),
  );
  await render(<App />);
  await screen.findByRole('button', { name: 'Where do you want to go?' });
}

describe('accounts and guest access', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(async () => {
    await AsyncStorage.clear();
    await sessionManager.clear();
    routes = {
      'GET /api/v1/profile': () => ({ status: 200, body: profile }),
      'POST /api/v1/auth/logout': () => ({ status: 204 }),
    };
    installFakeApi();
  });

  afterAll(() => {
    globalThis.fetch = originalFetch;
  });

  it('asks guests to sign in on Saved, Bookings and Profile', async () => {
    const user = userEvent.setup();
    await launch();

    await user.press(tab('Saved'));
    expect(await screen.findByRole('header', { name: 'Sign in to save places' })).toBeOnTheScreen();

    await user.press(tab('Bookings'));
    expect(
      await screen.findByRole('header', { name: 'Sign in to see your bookings' }),
    ).toBeOnTheScreen();

    await user.press(tab('Profile'));
    expect(await screen.findByText('Guest')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Delete account' })).not.toBeOnTheScreen();
    expect(requests).toEqual([]);
  });

  it('signs a new user in with a code, asks for a name and keeps the session', async () => {
    routes['POST /api/v1/auth/otp/request'] = () => ({
      status: 202,
      body: {
        challengeId: 'c1',
        expiresAt: minutesFromNow(5),
        resendAfterSeconds: 30,
        maskedDestination: '+91 ••••••3210',
      },
    });
    routes['POST /api/v1/auth/otp/verify'] = () => ({ status: 200, body: tokensFor('', true) });
    routes['PATCH /api/v1/profile'] = () => ({ status: 200, body: profile });
    const user = userEvent.setup();
    await launch();

    await user.press(tab('Saved'));
    await user.press(await screen.findByRole('button', { name: 'Sign in' }));
    await user.type(await screen.findByLabelText('Mobile number'), '98765 43210');
    await user.press(screen.getByRole('button', { name: 'Send code' }));

    expect(await screen.findByText('We sent a 6-digit code to +91 ••••••3210.')).toBeOnTheScreen();
    expect(requests[0]).toMatchObject({
      route: 'POST /api/v1/auth/otp/request',
      body: { channel: 'sms', destination: '9876543210' },
    });

    await user.type(screen.getByLabelText('6-digit code'), '123456');
    expect(
      await screen.findByRole('header', { name: 'What should we call you?' }),
    ).toBeOnTheScreen();
    expect(requests[1]).toMatchObject({
      route: 'POST /api/v1/auth/otp/verify',
      body: { challengeId: 'c1', code: '123456' },
    });

    await user.type(screen.getByLabelText('Your name'), 'Asha');
    await user.press(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByRole('header', { name: 'Nothing saved yet' })).toBeOnTheScreen();
    expect(requests.find((request) => request.route === 'PATCH /api/v1/profile')).toMatchObject({
      body: { displayName: 'Asha' },
      authorization: 'Bearer access-1',
    });
    await waitFor(async () => {
      expect((await loadSession())?.user.displayName).toBe('Asha');
    });
  });

  it('shows a clear message when the code is wrong and lets the user retry', async () => {
    routes['POST /api/v1/auth/otp/request'] = () => ({
      status: 202,
      body: {
        challengeId: 'c1',
        expiresAt: minutesFromNow(5),
        resendAfterSeconds: 30,
        maskedDestination: '+91 ••••••3210',
      },
    });
    routes['POST /api/v1/auth/otp/verify'] = () => ({
      status: 400,
      body: { code: 'invalid_code' },
    });
    const user = userEvent.setup();
    await launch();

    await user.press(tab('Saved'));
    await user.press(await screen.findByRole('button', { name: 'Sign in' }));
    await user.type(await screen.findByLabelText('Mobile number'), '9876543210');
    await user.press(screen.getByRole('button', { name: 'Send code' }));
    await user.type(await screen.findByLabelText('6-digit code'), '000000');

    expect(
      await screen.findByText("That code isn't right. Check it and try again."),
    ).toBeOnTheScreen();
    expect(await loadSession()).toBeNull();
  });

  it('rejects a mobile number that is not valid before calling the server', async () => {
    const user = userEvent.setup();
    await launch();

    await user.press(tab('Saved'));
    await user.press(await screen.findByRole('button', { name: 'Sign in' }));
    await user.type(await screen.findByLabelText('Mobile number'), '12345');
    await user.press(screen.getByRole('button', { name: 'Send code' }));

    expect(await screen.findByText('Enter a 10-digit mobile number.')).toBeOnTheScreen();
    expect(requests).toEqual([]);
  });

  describe('with a saved session', () => {
    beforeEach(async () => {
      await sessionManager.start(tokensFor('Asha', false));
    });

    it('restores the session on launch and shows the account on Profile', async () => {
      const user = userEvent.setup();
      await sessionManager.restore();
      await launch();

      await user.press(tab('Profile'));

      expect(await screen.findByText('Asha')).toBeOnTheScreen();
      expect(await screen.findByText('+91 98765 43210')).toBeOnTheScreen();
      expect(screen.getByRole('button', { name: 'Edit profile' })).toBeOnTheScreen();
      await user.press(tab('Saved'));
      expect(await screen.findByRole('header', { name: 'Nothing saved yet' })).toBeOnTheScreen();
    });

    it('shows saved places and trips together on the Saved tab', async () => {
      routes['GET /api/v1/saved-places'] = () => ({
        status: 200,
        body: [
          {
            place: {
              id: 'place-1',
              slug: 'paradise-beach',
              name: 'Paradise Beach',
              categoryCode: 'beach',
              latitude: null,
              longitude: null,
              coverImage: null,
              distanceKm: null,
            },
            savedAt: '2026-10-01T00:00:00Z',
          },
        ],
      });
      routes['GET /api/v1/itineraries'] = () => ({
        status: 200,
        body: [
          {
            id: 'trip-1',
            title: 'Weekend trip',
            startDate: null,
            endDate: null,
            status: 'saved',
            source: 'manual',
            isShared: false,
            stopCount: 2,
            createdAt: '2026-10-01T00:00:00Z',
            updatedAt: '2026-10-01T00:00:00Z',
          },
        ],
      });
      const user = userEvent.setup();
      await launch();

      await user.press(tab('Saved'));

      expect(await screen.findByText('Weekend trip')).toBeOnTheScreen();
      expect(screen.getByText('2 stops')).toBeOnTheScreen();
      expect(screen.getByText('Paradise Beach')).toBeOnTheScreen();
    });

    it('signs out, clears the stored session and returns to guest views', async () => {
      const user = userEvent.setup();
      await launch();

      await user.press(tab('Profile'));
      await user.press(await screen.findByRole('button', { name: 'Sign out' }));

      expect(await screen.findByText("You're signed out.")).toBeOnTheScreen();
      expect(await screen.findByText('Guest')).toBeOnTheScreen();
      expect(await loadSession()).toBeNull();
      expect(requests.some((request) => request.route === 'POST /api/v1/auth/logout')).toBe(true);
    });

    it('signs the user out when the server ends the session', async () => {
      routes['GET /api/v1/profile'] = () => ({ status: 401 });
      routes['POST /api/v1/auth/refresh'] = () => ({
        status: 401,
        body: { code: 'invalid_refresh_token' },
      });
      await launch();

      expect(
        await screen.findByText('Your session has ended. Sign in again to continue.'),
      ).toBeOnTheScreen();
      expect(await loadSession()).toBeNull();
    });

    it('deletes the account after re-confirming with a code', async () => {
      routes['POST /api/v1/account/deletion/code'] = () => ({
        status: 202,
        body: {
          challengeId: 'd1',
          expiresAt: minutesFromNow(5),
          resendAfterSeconds: 30,
          maskedDestination: '+91 ••••••3210',
        },
      });
      routes['POST /api/v1/account/deletion/confirm'] = () => ({ status: 204 });
      const user = userEvent.setup();
      await launch();

      await user.press(tab('Profile'));
      await user.press(await screen.findByRole('button', { name: 'Delete account' }));
      await user.press(await screen.findByRole('button', { name: 'Send confirmation code' }));
      await user.type(await screen.findByLabelText('6-digit code'), '123456');
      await user.press(screen.getByRole('button', { name: 'Delete my account' }));

      expect(await screen.findByText('Your account has been deleted.')).toBeOnTheScreen();
      expect(await screen.findByText('Guest')).toBeOnTheScreen();
      expect(await loadSession()).toBeNull();
      expect(
        requests.find((r) => r.route === 'POST /api/v1/account/deletion/confirm'),
      ).toMatchObject({ body: { challengeId: 'd1', code: '123456' } });
    });

    it('explains what must finish first when deletion is blocked', async () => {
      routes['POST /api/v1/account/deletion/code'] = () => ({
        status: 409,
        body: { code: 'deletion_blocked', reasons: ['An order is in progress'] },
      });
      const user = userEvent.setup();
      await launch();

      await user.press(tab('Profile'));
      await user.press(await screen.findByRole('button', { name: 'Delete account' }));
      await user.press(await screen.findByRole('button', { name: 'Send confirmation code' }));

      expect(
        await screen.findByText(/You have bookings, orders or refunds in progress/),
      ).toBeOnTheScreen();
      expect(await loadSession()).not.toBeNull();
    });
  });
});
